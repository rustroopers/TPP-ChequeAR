using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        public static string Format(IEnumerable<QuestionnaireResponseItem> items)
        {
            var values = (items ?? Enumerable.Empty<QuestionnaireResponseItem>())
                .Select(Format).Where(v => v != Empty).ToList();
            return values.Count > 0 ? string.Join(", ", values) : Empty;
        }

        public static string Format(QuestionnaireResponseItem item)
        {
            if (item == null) return Empty;
            if (item.ValueBoolean.HasValue) return item.ValueBoolean.Value ? "Sí" : "No";
            if (item.ValueInteger.HasValue) return item.ValueInteger.Value.ToString(Culture);
            if (item.ValueDecimal.HasValue) return item.ValueDecimal.Value.ToString(Culture);
            if (item.ValueDate.HasValue) return item.ValueDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(item.ValueString)) return item.ValueString;
            if (!string.IsNullOrEmpty(item.ValueCode)) return item.ValueCode;
            return Empty;
        }
    }
}
