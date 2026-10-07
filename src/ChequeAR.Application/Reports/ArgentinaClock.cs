using System;

namespace ChequeAR.Application.Reports
{
    /// <summary>
    /// Conversión entre UTC (como se guardan los instantes en la base) y hora de Argentina
    /// (UTC-3, sin horario de verano). Vive en Application porque el día que elige una
    /// persona en un filtro es un día argentino, y decidir qué instantes abarca es parte de
    /// la consulta, no de la pantalla.
    /// </summary>
    public static class ArgentinaClock
    {
        private static readonly TimeZoneInfo Tz = Resolve();

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
        public static DateTime FromUtc(DateTime utc)
        {
            // Las columnas DATE de Oracle vuelven como Unspecified: se guardaron en UTC,
            // así que se tratan como tales.
            var asUtc = utc.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(utc, DateTimeKind.Utc)
                : utc.ToUniversalTime();
            return TimeZoneInfo.ConvertTimeFromUtc(asUtc, Tz);
        }

        /// <summary>Fecha/hora local de Argentina a UTC (para comparar contra columnas guardadas en UTC).</summary>
        public static DateTime ToUtc(DateTime argLocal)
        {
            var unspecified = DateTime.SpecifyKind(argLocal, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(unspecified, Tz);
        }
    }
}
