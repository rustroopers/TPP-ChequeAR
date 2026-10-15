using System;
using System.Net.Http;
using System.Security.Cryptography;

namespace ChequeAR.Infrastructure.Fhir
{
    /// HttpClient para el servidor terminológico. Sin huella configurada valida TLS como
    /// siempre (cadena de CAs). Con huella hace pinning: acepta sólo el certificado cuyo
    /// SHA-256 coincide, sea o no autofirmado, y rechaza cualquier otro aunque tenga una
    /// cadena válida. Es lo que permite usar el Snowstorm Lite propio sin dominio ni CA.
    public static class TerminologyHttp
    {
        public static HttpClient Create(TimeSpan timeout, string pinnedSha256)
        {
            var pin = Normalize(pinnedSha256);
            if (pin == null) return new HttpClient { Timeout = timeout };

            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) =>
                {
                    if (cert == null) return false;
                    using (var sha = SHA256.Create())
                    {
                        var hash = BitConverter.ToString(sha.ComputeHash(cert.RawData)).Replace("-", "");
                        return string.Equals(hash, pin, StringComparison.OrdinalIgnoreCase);
                    }
                }
            };
            return new HttpClient(handler) { Timeout = timeout };
        }

        // Acepta el formato de openssl ("AB:CD:...") o hex corrido.
        private static string Normalize(string sha256)
        {
            if (string.IsNullOrWhiteSpace(sha256)) return null;
            return sha256.Replace(":", "").Replace(" ", "").Trim().ToUpperInvariant();
        }
    }
}
