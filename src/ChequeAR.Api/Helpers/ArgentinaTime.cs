using System;
using System.Globalization;
using ChequeAR.Application.Reports;

namespace ChequeAR.Api.Helpers
{
    /// <summary>
    /// Convierte instantes UTC (como se guardan en la base) a hora de Argentina
    /// (UTC-3, sin horario de verano) para mostrarlos en la UI. Centralizado para que
    /// toda la app muestre la misma zona horaria, sin depender de la del servidor.
    /// </summary>
    public static class ArgentinaTime
    {
        private static readonly CultureInfo Ar = new CultureInfo("es-AR");

        // La conversión en sí vive en Application (ArgentinaClock): los reportes la necesitan
        // para armar la consulta. Acá queda el formato de pantalla.

        /// <summary>Instante (asumido UTC si no tiene Kind) llevado a hora de Argentina.</summary>
        public static DateTime From(DateTime utc) => ArgentinaClock.FromUtc(utc);

        /// <summary>Formatea un instante UTC directamente en hora de Argentina.</summary>
        public static string Format(DateTime utc, string format) => From(utc).ToString(format);

        /// <summary>Convierte una fecha/hora local de Argentina a UTC (para comparar contra columnas guardadas en UTC).</summary>
        public static DateTime ToUtc(DateTime argLocal) => ArgentinaClock.ToUtc(argLocal);

        /// <summary>
        /// Fecha "yyyy-MM-dd" de un filtro (día local de Argentina) a límite UTC. Con
        /// endOfDay=true devuelve el comienzo del día siguiente, para usar como cota superior
        /// exclusiva. Null si el texto no es una fecha válida.
        /// </summary>
        public static DateTime? ParseFilterDateToUtc(string date, bool endOfDay)
        {
            if (!DateTime.TryParseExact((date ?? string.Empty).Trim(), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return null;
            return ToUtc(endOfDay ? d.Date.AddDays(1) : d.Date);
        }

        /// <summary>
        /// Si "desde" quedó después de "hasta" (rango invertido, tipeado a mano en la URL o
        /// pegado sin pasar por el date-range picker), los intercambia: nunca debe llegar a
        /// filtrarse con un rango negativo.
        /// </summary>
        public static void NormalizeFilterRange(ref string from, ref string to)
        {
            var fd = ParseFilterDateToUtc(from, endOfDay: false);
            var td = ParseFilterDateToUtc(to, endOfDay: false);
            if (fd.HasValue && td.HasValue && fd.Value > td.Value)
            {
                var tmp = from; from = to; to = tmp;
            }
        }

        /// <summary>Fecha en el formato estándar de la UI: dd/mm/yyyy (ej: 28/07/2026).</summary>
        public static string FormatDate(DateTime utc)
            => From(utc).ToString("dd/MM/yyyy", Ar);

        /// <summary>Fecha y hora en el formato estándar de la UI: dd/mm/yyyy HH:mm hrs (ej: 28/07/2026 18:31 hrs).</summary>
        public static string FormatDateTime(DateTime utc)
            => From(utc).ToString("dd/MM/yyyy HH:mm", Ar) + " hrs";
    }
}
