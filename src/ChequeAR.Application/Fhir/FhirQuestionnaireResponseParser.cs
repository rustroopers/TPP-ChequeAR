using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ChequeAR.Domain.Entities;
using Hl7.Fhir.Model;
using DomainResponse = ChequeAR.Domain.Entities.QuestionnaireResponse;
using DomainResponseItem = ChequeAR.Domain.Entities.QuestionnaireResponseItem;
using FhirResponse = Hl7.Fhir.Model.QuestionnaireResponse;

namespace ChequeAR.Application.Fhir
{
    /// <summary>
    /// Inverso de <see cref="FhirQuestionnaireResponseBuilder"/>: reconstruye la ficha de
    /// dominio a partir del recurso que envía el renderizador al guardar.
    /// </summary>
    public static class FhirQuestionnaireResponseParser
    {
        public static DomainResponse ToDomain(FhirResponse resource)
        {
            if (resource == null) return null;

            var r = new DomainResponse
            {
                QuestionnaireId = QuestionnaireIdFrom(resource.Questionnaire),
                QuestionnaireVersionId = ChequeARExtensions.GetQuestionnaireVersionId(resource) ?? Guid.Empty,
                PatientHospitalId = resource.Subject?.Identifier?.Value
            };

            AddItems(r, resource.Item);
            return r;
        }

        /// <summary>
        /// Recorre también los ítems anidados (secciones, y preguntas dentro de una respuesta):
        /// el dominio guarda las respuestas planas, por linkId.
        /// </summary>
        private static void AddItems(DomainResponse r, IEnumerable<FhirResponse.ItemComponent> items)
        {
            foreach (var item in items ?? Enumerable.Empty<FhirResponse.ItemComponent>())
            {
                var answers = item.Answer ?? new List<FhirResponse.AnswerComponent>();
                var nested = item.Item != null && item.Item.Count > 0;

                // Un item con varias answer vuelve a ser varios registros con el mismo linkId:
                // es la forma en que el dominio guarda una opción múltiple.
                if (answers.Count == 0 && !nested) r.Items.Add(new DomainResponseItem { LinkId = item.LinkId });
                foreach (var answer in answers)
                {
                    if (answer.Value != null) r.Items.Add(ToItem(item.LinkId, answer.Value));
                    AddItems(r, answer.Item);
                }
                AddItems(r, item.Item);
            }
        }

        /// <summary>
        /// El canonical es .../Questionnaire/{guid}. Se toma el último segmento; si no hay un
        /// Guid legible se devuelve Guid.Empty y la validación del servicio lo rechaza — mejor
        /// que adivinar a qué cuestionario pertenece una ficha clínica.
        /// </summary>
        private static Guid QuestionnaireIdFrom(string canonical)
        {
            if (string.IsNullOrWhiteSpace(canonical)) return Guid.Empty;
            var sinVersion = canonical.Split('|')[0].TrimEnd('/');
            var ultimo = sinVersion.Substring(sinVersion.LastIndexOf('/') + 1);
            return Guid.TryParse(ultimo, out var id) ? id : Guid.Empty;
        }

        private static DomainResponseItem ToItem(string linkId, DataType value)
        {
            var item = new DomainResponseItem { LinkId = linkId };

            switch (value)
            {
                // Calificado: el dominio también tiene una entidad Attachment.
                case Hl7.Fhir.Model.Attachment a:
                    // El archivo ya está subido: la URL referencia /api/attachments/{id}.
                    item.AttachmentId = AttachmentIdFrom(a.Url);
                    item.ValueString = a.Title;
                    break;
                case FhirBoolean b: item.ValueBoolean = b.Value; break;
                case Integer i: item.ValueInteger = i.Value; break;
                case FhirDecimal d: item.ValueDecimal = d.Value; break;
                case FhirDateTime dt: item.ValueDate = ParseDate(dt.Value); break;
                case Date fecha: item.ValueDate = ParseDate(fecha.Value); break;
                case Coding c: item.ValueCode = c.Code; break;
                case FhirString s: item.ValueString = s.Value; break;
            }
            return item;
        }

        private static Guid? AttachmentIdFrom(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            var ultimo = url.TrimEnd('/');
            ultimo = ultimo.Substring(ultimo.LastIndexOf('/') + 1);
            return Guid.TryParse(ultimo, out var id) ? id : (Guid?)null;
        }

        /// <summary>
        /// Se guarda la hora de Argentina, que es como la carga el renderer. Una fecha y hora con
        /// otra zona se convierte; sin convertir quedaría corrida según la zona del servidor.
        /// </summary>
        private static DateTime? ParseDate(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var conZona = raw.Length > 10 && (raw.EndsWith("Z") || raw.LastIndexOf('+') > 10 || raw.LastIndexOf('-') > 10);
            if (conZona && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
                return dto.ToOffset(TimeSpan.FromHours(-3)).DateTime;
            return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed : (DateTime?)null;
        }
    }
}
