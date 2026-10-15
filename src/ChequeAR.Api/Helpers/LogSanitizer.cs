using System;
using System.Text.RegularExpressions;

namespace ChequeAR.Api.Helpers
{
    /// <summary>Ruta para el log, sin query string y con la HC del paciente enmascarada.</summary>
    public static class LogSanitizer
    {
        private static readonly Regex PatientSegment = new Regex(
            @"(/(patients?|Patient)/)[^/?#]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

        public static string Path(string pathOrUrl)
        {
            if (string.IsNullOrEmpty(pathOrUrl)) return pathOrUrl;
            var path = pathOrUrl;
            var cut = path.IndexOfAny(new[] { '?', '#' });
            if (cut >= 0) path = path.Substring(0, cut);
            try
            {
                return PatientSegment.Replace(path, "$1***");
            }
            catch (RegexMatchTimeoutException)
            {
                return "(ruta omitida)";
            }
        }

        public static string Path(Uri uri) => uri == null ? null : Path(uri.AbsolutePath);
    }
}
