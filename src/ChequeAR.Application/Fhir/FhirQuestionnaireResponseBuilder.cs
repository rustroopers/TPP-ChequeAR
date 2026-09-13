using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ChequeAR.Domain.Enums;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using DomainResponse = ChequeAR.Domain.Entities.QuestionnaireResponse;
using DomainResponseItem = ChequeAR.Domain.Entities.QuestionnaireResponseItem;
using FhirResponse = Hl7.Fhir.Model.QuestionnaireResponse;

namespace ChequeAR.Application.Fhir
{
    /// <summary>
    /// Traduce una respuesta de dominio al recurso FHIR R5 QuestionnaireResponse, que es la
    /// representación canónica de la ficha y lo que valida $finalize.
    /// </summary>
    public static class FhirQuestionnaireResponseBuilder
    {
        public const string PatientIdentifierSystem = "https://chequear.garrahan/patient-hospital-id";
        private const string QuestionnaireBaseUrl = "https://chequear.garrahan/Questionnaire/";

        public static string Build(
            DomainResponse response, RenderableQuestionnaire pinnedVersion, PatientDemographicsDto patient = null)
            => new FhirJsonSerializer().SerializeToString(ToResource(response, pinnedVersion, patient));

        public static FhirResponse ToResource(
            DomainResponse response, RenderableQuestionnaire pinnedVersion, PatientDemographicsDto patient = null)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));

            var resource = new FhirResponse
            {
                Id = response.Id.ToString(),
                Questionnaire = QuestionnaireBaseUrl + response.QuestionnaireId,
                Status = FhirStatus(response.Status),
                Authored = response.AuthoredAt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                Subject = new ResourceReference
                {
                    Identifier = new Identifier(PatientIdentifierSystem, response.PatientHospitalId),
                    Display = patient?.FullName ?? response.PatientHospitalId
                }
            };

            // Ancla la ficha a la versión con la que se completó. Sin equivalente nativo:
            // el dominio ancla por Guid de versión, no por número (ver research.md R3).
            ChequeARExtensions.SetQuestionnaireVersionId(resource, response.QuestionnaireVersionId);

            if (!string.IsNullOrWhiteSpace(response.AuthorUserId))
                resource.Author = new ResourceReference { Display = response.AuthorUserId };

            AddContainedPatient(resource, patient);

            foreach (var item in BuildItems(response, pinnedVersion)) resource.Item.Add(item);
            return resource;
        }

        /// <summary>
        /// Paciente embebido. No es decorativo: cuando el hospital no responde, este Patient
        /// contenido es la ÚNICA forma de saber de quién es la ficha — lo lee PatientService
        /// en modo degradado.
        /// </summary>
        private static void AddContainedPatient(FhirResponse resource, PatientDemographicsDto patient)
        {
            if (patient == null || string.IsNullOrWhiteSpace(patient.FullName)) return;

            var contained = new Patient
            {
                Id = "p",
                Name = { new HumanName { Text = patient.FullName } }
            };
            if (!string.IsNullOrWhiteSpace(patient.Gender))
                contained.GenderElement = new Code<AdministrativeGender>(
                    Hl7.Fhir.Utility.EnumUtility.ParseLiteral<AdministrativeGender>(patient.Gender));
            if (patient.BirthDate.HasValue)
                contained.BirthDate = patient.BirthDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            resource.Contained.Add(contained);
        }

        private static List<FhirResponse.ItemComponent> BuildItems(
            DomainResponse response, RenderableQuestionnaire pinnedVersion)
        {
            // Los textos vienen de la VERSIÓN ANCLADA, no de la entidad viva: si alguien
            // renombró una pregunta después, la ficha conserva lo que el médico leyó.
            var textByLinkId = (pinnedVersion?.Groups ?? new List<RenderableGroup>())
                .SelectMany(g => g.Questions)
                .Where(x => !string.IsNullOrWhiteSpace(x.LinkId))
                .GroupBy(x => x.LinkId)
                .ToDictionary(g => g.Key, g => g.First().Text);

            var items = new List<FhirResponse.ItemComponent>();

            // Un linkId puede repetirse en el dominio (opción múltiple: un registro por
            // opción marcada). En FHIR eso es UN item con varias entradas en answer.
            foreach (var grupo in (response.Items ?? new List<DomainResponseItem>())
                         .Where(i => !string.IsNullOrWhiteSpace(i.LinkId))
                         .GroupBy(i => i.LinkId))
            {
                var item = new FhirResponse.ItemComponent { LinkId = grupo.Key };

                if (textByLinkId.TryGetValue(grupo.Key, out var text) && !string.IsNullOrWhiteSpace(text))
                    item.Text = text;

                foreach (var valor in grupo.Select(Answer).Where(a => a != null))
                    item.Answer.Add(new FhirResponse.AnswerComponent { Value = valor });

                // Una pregunta sin responder se serializa SIN answer, no con uno vacío.
                items.Add(item);
            }
            return items;
        }

        private static DataType Answer(DomainResponseItem item)
        {
            // El adjunto se guarda por referencia: el archivo ya está subido.
            if (item.AttachmentId.HasValue)
            {
                return new Attachment
                {
                    Url = "/api/attachments/" + item.AttachmentId.Value,
                    Title = item.ValueString
                };
            }
            if (item.ValueBoolean.HasValue) return new FhirBoolean(item.ValueBoolean.Value);
            if (item.ValueInteger.HasValue) return new Integer(item.ValueInteger.Value);
            if (item.ValueDecimal.HasValue) return new FhirDecimal(item.ValueDecimal.Value);
            if (item.ValueDate.HasValue)
                return new FhirDateTime(item.ValueDate.Value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(item.ValueCode))
                return new Coding(null, item.ValueCode, item.ValueCode);
            if (!string.IsNullOrEmpty(item.ValueString)) return new FhirString(item.ValueString);
            return null;   // sin responder
        }

        private static FhirResponse.QuestionnaireResponseStatus FhirStatus(ResponseStatus status)
        {
            switch (status)
            {
                case ResponseStatus.Completed: return FhirResponse.QuestionnaireResponseStatus.Completed;
                case ResponseStatus.Amended:   return FhirResponse.QuestionnaireResponseStatus.Amended;
                case ResponseStatus.EnteredInError: return FhirResponse.QuestionnaireResponseStatus.EnteredInError;
                default: return FhirResponse.QuestionnaireResponseStatus.InProgress;
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
