using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ChequeAR.Application.Fhir;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Application.Services
{
    /// <summary>
    /// Texto legible de una respuesta, para mostrarla (ficha en lectura) y para auditarla
    /// (diff de sesión, foto de la finalización). Un único formato en los dos lugares: antes
    /// había dos copias y la de la vista dependía de la cultura del hilo ("3,5") mientras la
    /// de auditoría usaba la invariante ("3.5").
    /// </summary>
    public static class ResponseAnswerFormatter
    {
        /// <summary>Marca de "sin respuesta".</summary>
        public const string Empty = "—";

        // Explícita y no la del hilo: el resultado no puede cambiar según dónde corra.
        private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("es-AR");

        /// <summary>Las respuestas de una pregunta (varias si es opción múltiple), separadas por coma.</summary>
        /// <param name="question">Si viene, las opciones se muestran con su etiqueta y no con el código.</param>
        public static string Format(IEnumerable<QuestionnaireResponseItem> items, RenderableQuestion question = null)
        {
            var values = (items ?? Enumerable.Empty<QuestionnaireResponseItem>())
                .Select(i => Format(i, question)).Where(v => v != Empty).ToList();
            return values.Count > 0 ? string.Join(", ", values) : Empty;
        }

        public static string Format(QuestionnaireResponseItem item, RenderableQuestion question = null)
        {
            if (item == null) return Empty;
            if (item.ValueBoolean.HasValue) return item.ValueBoolean.Value ? "Sí" : "No";
            if (item.ValueInteger.HasValue) return item.ValueInteger.Value.ToString(Culture);
            if (item.ValueDecimal.HasValue) return item.ValueDecimal.Value.ToString(Culture);
            if (item.ValueDate.HasValue)
                return item.ValueDate.Value.ToString(
                    item.ValueDate.Value.TimeOfDay == TimeSpan.Zero ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(item.ValueString)) return item.ValueString;
            if (!string.IsNullOrEmpty(item.ValueCode))
                return question?.FindOption(item.ValueCode)?.Display ?? item.ValueCode;
            return Empty;
        }
    }
}
