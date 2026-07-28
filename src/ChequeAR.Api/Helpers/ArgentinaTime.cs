using System;
using System.Globalization;

namespace ChequeAR.Api.Helpers
{
    /// <summary>
    /// Convierte instantes UTC (como se guardan en la base) a hora de Argentina
    /// (UTC-3, sin horario de verano) para mostrarlos en la UI. Centralizado para que
    /// toda la app muestre la misma zona horaria, sin depender de la del servidor.
    /// </summary>
    public static class ArgentinaTime
    {
        private static readonly TimeZoneInfo Tz = Resolve();
        private static readonly CultureInfo Ar = new CultureInfo("es-AR");

        private static TimeZoneInfo Resolve()
        {
            // El id cambia según el SO: Windows usa "Argentina Standard Time",
            // Linux/IANA usa "America/Argentina/Buenos_Aires".
            foreach (var id in new[] { "Argentina Standard Time", "America/Argentina/Buenos_Aires" })
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }
            // Fallback: offset fijo (Argentina no aplica DST).
            return TimeZoneInfo.CreateCustomTimeZone("AR", TimeSpan.FromHours(-3), "Argentina", "Argentina");
        }

        /// <summary>Instante (asumido UTC si no tiene Kind) llevado a hora de Argentina.</summary>
        public static DateTime From(DateTime utc)
        {
            // Las columnas DATE de Oracle vuelven como Unspecified: se guardaron en UTC,
            // así que las tratamos como tales.
            var asUtc = utc.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(utc, DateTimeKind.Utc)
                : utc.ToUniversalTime();
            return TimeZoneInfo.ConvertTimeFromUtc(asUtc, Tz);
        }

        /// <summary>Formatea un instante UTC directamente en hora de Argentina.</summary>
        public static string Format(DateTime utc, string format) => From(utc).ToString(format);

        /// <summary>Fecha en el formato estándar de la UI: dd/mm/yyyy (ej: 28/07/2026).</summary>
        public static string FormatDate(DateTime utc)
            => From(utc).ToString("dd/MM/yyyy", Ar);

        /// <summary>Fecha y hora en el formato estándar de la UI: dd/mm/yyyy HH:mm hrs (ej: 28/07/2026 18:31 hrs).</summary>
        public static string FormatDateTime(DateTime utc)
            => From(utc).ToString("dd/MM/yyyy HH:mm", Ar) + " hrs";
    }
}
