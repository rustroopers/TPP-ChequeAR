using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace ChequeAR.Application.Security
{
    /// <summary>
    /// Hashing de contraseñas para el login local (ver LocalCredentialAuthenticator). Vive en
    /// Application -no en Infrastructure ni Domain- por el mismo precedente que
    /// RefreshTokenService: cripto de la BCL, cero paquetes, testeable dentro de la barrera
    /// Domain+Application (LayerDependencyTests).
    ///
    /// PBKDF2-HMAC-SHA1, no SHA256: los 5 proyectos targetean net471, y
    /// Rfc2898DeriveBytes ahí sólo expone el constructor que deriva con HMAC-SHA1 (el
    /// overload con HashAlgorithmName recién existe desde net472). Se compensa con 210.000
    /// iteraciones (la recomendación OWASP para SHA1). Este login es provisorio -se reemplaza
    /// el día que exista la API de autenticación del Garrahan (ver ICredentialAuthenticator)-
    /// así que no se justifica retargetear el árbol de proyectos sólo por esto.
    /// </summary>
    public static class PasswordHasher
    {
        private const string Algorithm = "pbkdf2-sha1";
        private const int Iterations = 210_000;
        private const int SaltSize = 16; // bytes
        private const int HashSize = 32; // bytes

        [SuppressMessage("Security", "CA5379:Do Not Use Weak Key Derivation Function Algorithm",
            Justification = "net471 no expone el ctor de Rfc2898DeriveBytes con HashAlgorithmName (recién en net472). " +
                             "210.000 iteraciones compensan SHA1 (línea OWASP). Login local provisorio: se reemplaza " +
                             "cuando exista la API de autenticación del Hospital Garrahan.")]
        [SuppressMessage("Security", "CA5387:Do Not Use Weak Key Derivation Function With Insufficient Iteration Count",
            Justification = "210.000 iteraciones ya está por encima de la recomendación OWASP para PBKDF2-HMAC-SHA1.")]
        private static byte[] Derive(string password, byte[] salt, int iterations, int size)
        {
            using (var kdf = new Rfc2898DeriveBytes(password ?? "", salt, iterations))
            {
                return kdf.GetBytes(size);
            }
        }

        /// <summary>Genera un hash nuevo con un salt aleatorio. Formato: algo$iteraciones$salt$hash (base64).</summary>
        public static string Hash(string password)
        {
            byte[] salt;
            using (var rng = RandomNumberGenerator.Create())
            {
                salt = new byte[SaltSize];
                rng.GetBytes(salt);
            }
            var hash = Derive(password, salt, Iterations, HashSize);
            return Algorithm + "$" + Iterations + "$" + Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(hash);
        }

        /// <summary>
        /// Compara la contraseña contra un hash codificado. Fail-closed ante CUALQUIER
        /// formato inválido (null, vacío, corrupto, algoritmo desconocido, base64 mal formado):
        /// nunca lanza, siempre devuelve false. Un usuario sin PasswordHash (null) o con un
        /// valor corrupto queda simplemente sin poder loguearse -no es una excepción no
        /// manejada tirando 500 en el login.
        /// </summary>
        public static bool Verify(string password, string encodedHash)
        {
            if (!TryParse(encodedHash, out var iterations, out var salt, out var expected)) return false;
            byte[] actual;
            try
            {
                actual = Derive(password, salt, iterations, expected.Length);
            }
            catch (CryptographicException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                // salt de menos de 8 bytes: Rfc2898DeriveBytes tira ArgumentException
                return false;
            }
            return FixedTimeEquals(actual, expected);
        }

        /// <summary>¿Este hash fue generado con los parámetros vigentes (algoritmo + iteraciones)?</summary>
        public static bool NeedsRehash(string encodedHash)
        {
            if (!TryParse(encodedHash, out var iterations, out _, out _)) return true;
            return iterations != Iterations;
        }

        private static bool TryParse(string encodedHash, out int iterations, out byte[] salt, out byte[] hash)
        {
            iterations = 0; salt = null; hash = null;
            if (string.IsNullOrEmpty(encodedHash)) return false;

            var parts = encodedHash.Split('$');
            if (parts.Length != 4) return false;
            if (!string.Equals(parts[0], Algorithm, StringComparison.Ordinal)) return false;
            if (!int.TryParse(parts[1], out iterations) || iterations <= 0 || iterations > 10_000_000) return false;

            try
            {
                salt = Convert.FromBase64String(parts[2]);
                hash = Convert.FromBase64String(parts[3]);
            }
            catch (FormatException)
            {
                return false;
            }
            return salt.Length > 0 && hash.Length > 0;
        }

        /// <summary>
        /// Comparación en tiempo constante. No hay CryptographicOperations.FixedTimeEquals en
        /// netfx: se recorre TODO el largo esperado sin cortar en la primera diferencia (a
        /// diferencia de SequenceEqual, que sí corta), para que el tiempo no filtre en qué
        /// byte empezó a diferir el hash.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null) return false;
            var diff = (uint)a.Length ^ (uint)b.Length;
            var len = Math.Min(a.Length, b.Length);
            for (int i = 0; i < len; i++)
                diff |= (uint)(a[i] ^ b[i]);
            return diff == 0;
        }
    }
}
