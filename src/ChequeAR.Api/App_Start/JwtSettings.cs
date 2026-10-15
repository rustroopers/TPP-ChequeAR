using System;
using System.Configuration;
using System.Security.Cryptography;
using System.Text;
using Serilog;

namespace ChequeAR.Api.App_Start
{
    /// <summary>
    /// Configuración del JWT, compartida por Startup (validación) y ServiceComposition (emisión).
    /// En Release no arranca sin secreto propio: appSettings file= ignora que falte el archivo.
    /// </summary>
    public static class JwtSettings
    {
        private const string ExamplePlaceholder = "REPLACE_WITH_ENV_OR_DPAPI_PROTECTED_SECRET";
        private const int MinSecretBytes = 32;

        private static readonly Lazy<Values> Current = new Lazy<Values>(Load);

        public static string Issuer => Current.Value.Issuer;
        public static string Audience => Current.Value.Audience;
        public static byte[] SigningKey => Current.Value.SigningKey;

        private sealed class Values
        {
            public string Issuer;
            public string Audience;
            public byte[] SigningKey;
        }

        private static Values Load()
        {
            var secret = ConfigurationManager.AppSettings["Jwt:SigningSecret"];
            var problem = Validate(secret);
            byte[] key;
            if (problem == null)
            {
                key = Encoding.UTF8.GetBytes(secret);
            }
            else
            {
#if DEBUG
                Log.Warning("Jwt:SigningSecret {Problem}: se usa una clave aleatoria por proceso (solo Debug). "
                            + "Configurarla en Web.Secrets.config.", problem);
                key = new byte[64];
                using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(key);
#else
                throw new ConfigurationErrorsException(
                    "Jwt:SigningSecret " + problem + ". La aplicación no arranca sin una clave de firma propia "
                    + "(mínimo " + MinSecretBytes + " bytes) en Web.Secrets.config o en los App Settings.");
#endif
            }

            return new Values
            {
                Issuer = ConfigurationManager.AppSettings["Jwt:Issuer"] ?? "https://chequear.garrahan",
                Audience = ConfigurationManager.AppSettings["Jwt:Audience"] ?? "chequear-api",
                SigningKey = key
            };
        }

        private static string Validate(string secret)
        {
            if (string.IsNullOrWhiteSpace(secret)) return "no está configurado";
            if (string.Equals(secret.Trim(), ExamplePlaceholder, StringComparison.Ordinal))
                return "es el valor de ejemplo de Web.Secrets.config.example";
            if (Encoding.UTF8.GetByteCount(secret) < MinSecretBytes)
                return "es demasiado corto (mínimo " + MinSecretBytes + " bytes)";
            return null;
        }
    }
}
