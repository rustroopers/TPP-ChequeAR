using System;
using System.Collections.Generic;
using System.Linq;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ChequeAR.Application.Fhir
{
    /// <summary>
    /// Serializa una respuesta de dominio al recurso FHIR R5 QuestionnaireResponse que se
    /// guarda en QuestionnaireResponse.FhirJson.
    ///
    /// Ese campo no lo escribía nadie: FinalizeAsync lo validaba y, al estar vacío, toda
    /// finalización terminaba en 422.
    /// </summary>
    public static class FhirQuestionnaireResponseBuilder
    {
        public static string Build(QuestionnaireResponse response, Questionnaire questionnaire, PatientDemographicsDto patient = null)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));

            var resource = new JObject
            {
                ["resourceType"] = "QuestionnaireResponse",
                ["id"] = response.Id.ToString(),
                ["questionnaire"] = "https://chequear.garrahan/Questionnaire/" + response.QuestionnaireId,
                ["status"] = FhirStatus(response.Status),
                ["authored"] = response.AuthoredAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["subject"] = new JObject
                {
                    ["identifier"] = new JObject
                    {
                        ["system"] = "https://chequear.garrahan/patient-hospital-id",
                        ["value"] = response.PatientHospitalId
                    },
                    ["display"] = patient?.FullName ?? response.PatientHospitalId
                }
            };

            if (!string.IsNullOrWhiteSpace(response.AuthorUserId))
                resource["author"] = new JObject { ["display"] = response.AuthorUserId };

            // Patient "contained": es lo que lee FhirPatientReader cuando el hospital no
            // responde y las respuestas propias son la única fuente de pacientes.
            if (patient != null && !string.IsNullOrWhiteSpace(patient.FullName))
            {
                var contained = new JObject
                {
                    ["resourceType"] = "Patient",
                    ["id"] = "p",
                    ["name"] = new JArray(new JObject { ["text"] = patient.FullName })
                };
                if (!string.IsNullOrWhiteSpace(patient.Gender)) contained["gender"] = patient.Gender;
                if (patient.BirthDate.HasValue) contained["birthDate"] = patient.BirthDate.Value.ToString("yyyy-MM-dd");
                resource["contained"] = new JArray(contained);
            }

            resource["item"] = BuildItems(response, questionnaire);
            return JsonConvert.SerializeObject(resource, Formatting.None);
        }

        private static JArray BuildItems(QuestionnaireResponse response, Questionnaire questionnaire)
        {
            var textByLinkId = (questionnaire?.Questions ?? new List<Question>())
                .Where(x => !string.IsNullOrWhiteSpace(x.LinkId))
                .GroupBy(x => x.LinkId)
                .ToDictionary(g => g.Key, g => g.First().Text);

            var items = new JArray();
            // Un linkId puede repetirse (opción múltiple: un item por opción marcada). En
            // FHIR eso es UN item con varias entradas en "answer".
            foreach (var group in (response.Items ?? new List<QuestionnaireResponseItem>())
                         .Where(i => !string.IsNullOrWhiteSpace(i.LinkId))
                         .GroupBy(i => i.LinkId))
            {
                var answers = new JArray();
                foreach (var value in group.Select(Answer).Where(a => a != null)) answers.Add(value);

                var item = new JObject { ["linkId"] = group.Key };
                if (textByLinkId.TryGetValue(group.Key, out var text) && !string.IsNullOrWhiteSpace(text))
                    item["text"] = text;
                // Una pregunta sin responder se serializa sin "answer", no con un answer vacío.
                if (answers.Count > 0) item["answer"] = answers;
                items.Add(item);
            }
            return items;
        }

        private static JObject Answer(QuestionnaireResponseItem item)
        {
            if (item.AttachmentId.HasValue)
            {
                return new JObject
                {
                    ["valueAttachment"] = new JObject
                    {
                        ["url"] = "/api/attachments/" + item.AttachmentId.Value,
                        ["title"] = item.ValueString
                    }
                };
            }
            if (item.ValueBoolean.HasValue) return new JObject { ["valueBoolean"] = item.ValueBoolean.Value };
            if (item.ValueInteger.HasValue) return new JObject { ["valueInteger"] = item.ValueInteger.Value };
            if (item.ValueDecimal.HasValue) return new JObject { ["valueDecimal"] = item.ValueDecimal.Value };
            if (item.ValueDate.HasValue)
                return new JObject { ["valueDateTime"] = item.ValueDate.Value.ToString("yyyy-MM-ddTHH:mm:ss") };
            if (!string.IsNullOrEmpty(item.ValueCode))
                return new JObject { ["valueCoding"] = new JObject { ["code"] = item.ValueCode, ["display"] = item.ValueCode } };
            if (!string.IsNullOrEmpty(item.ValueString)) return new JObject { ["valueString"] = item.ValueString };
            return null;   // sin responder
        }

        private static string FhirStatus(ResponseStatus status)
        {
            switch (status)
            {
                case ResponseStatus.Completed: return "completed";
                case ResponseStatus.Amended: return "amended";
                default: return "in-progress";
            }
        }
    }

    /// <summary>Datos del paciente para embeber; los resuelve quien tenga acceso al hospital.</summary>
    public class PatientDemographicsDto
    {
        public string FullName { get; set; }
        public string Gender { get; set; }
        public DateTime? BirthDate { get; set; }
    }
}
