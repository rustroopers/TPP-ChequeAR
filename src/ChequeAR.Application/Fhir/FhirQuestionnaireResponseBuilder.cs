using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ChequeAR.Domain.Enums;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
        public const string ProfileUrl = "https://chequear.garrahan/StructureDefinition/ChequeARQuestionnaireResponse";
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
                Meta = new Meta { Profile = new[] { ProfileUrl } },
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
        /// Rescata los datos del paciente del recurso que ya venía guardado, para no
        /// perderlos al regenerarlo. Null si no había ninguno.
        /// </summary>
        public static PatientDemographicsDto ContainedPatient(string existingFhirJson)
        {
            // Sólo se acepta un Patient "contained" de verdad. NO vale FhirPatientReader,
            // que cae a subject.display como nombre: como ahí escribimos la HC cuando no
            // hay nombre, regenerar el recurso al finalizar terminaba inventando un
            // paciente llamado "PAC-000001".
            if (string.IsNullOrWhiteSpace(existingFhirJson)) return null;
            try
            {
                var contained = JObject.Parse(existingFhirJson)["contained"] as JArray;
                foreach (var c in contained ?? new JArray())
                {
                    if ((string)c["resourceType"] != "Patient") continue;
                    var name = (string)c.SelectToken("name[0].text");
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    DateTime? birth = null;
                    var raw = (string)c["birthDate"];
                    if (!string.IsNullOrWhiteSpace(raw) && DateTime.TryParse(raw, out var parsed)) birth = parsed;

                    return new PatientDemographicsDto
                    {
                        FullName = name,
                        Gender = (string)c["gender"],
                        BirthDate = birth
                    };
                }
            }
            catch (JsonException) { /* recurso ilegible: sin datos de paciente */ }
            return null;
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

        /// <summary>
        /// Los ítems siguen la estructura de la versión anclada: cada sección es un item group con
        /// sus preguntas adentro, como pide R5. Las preguntas sueltas van en la raíz, igual que en
        /// el Questionnaire. Sin estructura, o para un linkId que no está en ella, quedan en la raíz.
        /// </summary>
        private static List<FhirResponse.ItemComponent> BuildItems(
            DomainResponse response, RenderableQuestionnaire pinnedVersion)
        {
            var groups = pinnedVersion?.Groups ?? new List<RenderableGroup>();
            var questionByLinkId = groups
                .SelectMany(g => g.Questions)
                .Where(x => !string.IsNullOrWhiteSpace(x.LinkId))
                .GroupBy(x => x.LinkId)
                .ToDictionary(g => g.Key, g => g.First());

            // Un linkId puede repetirse en el dominio (opción múltiple: un registro por
            // opción marcada). En FHIR eso es UN item con varias entradas en answer.
            var answered = new Dictionary<string, FhirResponse.ItemComponent>();
            var order = new List<string>();
            foreach (var grupo in (response.Items ?? new List<DomainResponseItem>())
                         .Where(i => !string.IsNullOrWhiteSpace(i.LinkId))
                         .GroupBy(i => i.LinkId))
            {
                questionByLinkId.TryGetValue(grupo.Key, out var question);
                var item = new FhirResponse.ItemComponent { LinkId = grupo.Key };
                // Los textos vienen de la VERSIÓN ANCLADA, no de la entidad viva: si alguien
                // renombró una pregunta después, la ficha conserva lo que el médico leyó.
                if (!string.IsNullOrWhiteSpace(question?.Text)) item.Text = question.Text;

                // Una pregunta sin responder se serializa SIN answer, no con uno vacío.
                foreach (var valor in grupo.Select(i => Answer(i, question)).Where(a => a != null))
                    item.Answer.Add(new FhirResponse.AnswerComponent { Value = valor });

                answered[grupo.Key] = item;
                order.Add(grupo.Key);
            }

            var items = new List<FhirResponse.ItemComponent>();
            var placed = new HashSet<string>();
            foreach (var g in groups)
            {
                var children = g.Questions
                    .Where(q => q.LinkId != null && answered.ContainsKey(q.LinkId) && placed.Add(q.LinkId))
                    .Select(q => answered[q.LinkId]).ToList();
                if (children.Count == 0) continue;
                if (g.Synthetic) { items.AddRange(children); continue; }

                var section = new FhirResponse.ItemComponent { LinkId = g.LinkId, Text = g.Title };
                section.Item.AddRange(children);
                items.Add(section);
            }
            items.AddRange(order.Where(placed.Add).Select(l => answered[l]));
            return items;
        }

        /// <summary>
        /// El answer[x] lo decide el tipo de la pregunta, no la columna donde quedó el valor: la
        /// escala se guarda como decimal pero en el Questionnaire es integer, y una fecha sin hora
        /// es valueDate. Sin pregunta, se usa el tipo que mandó el renderer o, si no, la columna.
        /// </summary>
        private static DataType Answer(DomainResponseItem item, RenderableQuestion question)
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

            var number = item.ValueDecimal ?? item.ValueInteger;
            switch (question?.Type ?? item.QuestionType)
            {
                case "integer":
                case "scale":
                    if (number.HasValue && number.Value == decimal.Truncate(number.Value)
                        && number.Value <= int.MaxValue && number.Value >= int.MinValue)
                        return new Integer((int)number.Value);
                    break;
                case "decimal":
                    if (number.HasValue) return new FhirDecimal(number.Value);
                    break;
                case "date":
                    if (item.ValueDate.HasValue) return DateOnly(item.ValueDate.Value);
                    break;
                case "dateTime":
                    if (item.ValueDate.HasValue) return DateTimeArgentina(item.ValueDate.Value);
                    break;
            }

            if (item.ValueBoolean.HasValue) return new FhirBoolean(item.ValueBoolean.Value);
            if (item.ValueInteger.HasValue) return new Integer(item.ValueInteger.Value);
            if (item.ValueDecimal.HasValue) return new FhirDecimal(item.ValueDecimal.Value);
            if (item.ValueDate.HasValue)
                return item.ValueDate.Value.TimeOfDay == TimeSpan.Zero
                    ? (DataType)DateOnly(item.ValueDate.Value)
                    : DateTimeArgentina(item.ValueDate.Value);
            if (!string.IsNullOrEmpty(item.ValueCode))
            {
                // Mismo Coding que el answerOption: es lo único que un consumidor puede comparar.
                var option = question?.FindOption(item.ValueCode);
                return option != null
                    ? new Coding(option.System, option.Code, option.Display)
                    : new Coding(null, item.ValueCode, item.ValueCode);
            }
            if (!string.IsNullOrEmpty(item.ValueString)) return new FhirString(item.ValueString);
            return null;   // sin responder
        }

        private static Date DateOnly(DateTime value)
            => new Date(value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        /// <summary>
        /// La fecha y hora se guarda como la tipeó el médico, en hora de Argentina (sin horario
        /// de verano). R5 exige la zona cuando hay hora.
        /// </summary>
        private static FhirDateTime DateTimeArgentina(DateTime value)
        {
            // Sin Kind es la hora tal como se cargó; con Kind, se convierte.
            var local = value.Kind == DateTimeKind.Unspecified ? value : value.ToUniversalTime().AddHours(-3);
            return new FhirDateTime(local.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) + "-03:00");
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
