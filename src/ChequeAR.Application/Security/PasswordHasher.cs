using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ChequeAR.Application.Security
{
    /// <summary>
    /// Hashing de contraseñas para el login local (ver LocalCredentialAuthenticator). Vive en
    /// Application -no en Infrastructure ni Domain- por el mismo precedente que
    /// RefreshTokenService: cripto de la BCL, cero paquetes, testeable dentro de la barrera
    /// Domain+Application (LayerDependencyTests).
    ///
    /// PBKDF2-HMAC-SHA256 con CNG (bcrypt.dll): en net471 Rfc2898DeriveBytes sólo deriva con
    /// SHA1, y un HMAC-SHA256 managed hace cada login varias veces más lento. Los hashes
    /// "pbkdf2-sha1" anteriores se siguen verificando y se regeneran al entrar.
    /// </summary>
    public static class PasswordHasher
    {
        private const string Algorithm = "pbkdf2-sha256";
        private const string LegacyAlgorithm = "pbkdf2-sha1";
        private const int Iterations = 600_000; // OWASP para PBKDF2-HMAC-SHA256
        private const int SaltSize = 16; // bytes
        private const int HashSize = 32; // bytes

        [SuppressMessage("Security", "CA5379:Do Not Use Weak Key Derivation Function Algorithm",
            Justification = "Sólo para verificar hashes anteriores; se regeneran con SHA256 al loguearse.")]
        [SuppressMessage("Security", "CA5387:Do Not Use Weak Key Derivation Function With Insufficient Iteration Count",
            Justification = "Sólo para verificar hashes anteriores, con las iteraciones con que se generaron.")]
        private static byte[] DeriveLegacy(string password, byte[] salt, int iterations, int size)
        {
            using (var kdf = new Rfc2898DeriveBytes(password ?? "", salt, iterations))
            {
                return kdf.GetBytes(size);
            }
        }

        private static byte[] Derive(string password, byte[] salt, int iterations, int size)
        {
            var pass = Encoding.UTF8.GetBytes(password ?? "");
            var output = new byte[size];
            var status = NativeMethods.BCryptOpenAlgorithmProvider(out var alg, "SHA256", null, NativeMethods.BCRYPT_ALG_HANDLE_HMAC_FLAG);
            if (status != 0) throw new CryptographicException(status);
            try
            {
                status = NativeMethods.BCryptDeriveKeyPBKDF2(alg, pass, pass.Length, salt, salt.Length,
                    (ulong)iterations, output, output.Length, 0);
                if (status != 0) throw new CryptographicException(status);
            }
            finally
            {
                _ = NativeMethods.BCryptCloseAlgorithmProvider(alg, 0);
                Array.Clear(pass, 0, pass.Length);
            }
            return output;
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
            if (!TryParse(encodedHash, out var algorithm, out var iterations, out var salt, out var expected)) return false;
            byte[] actual;
            try
            {
                actual = algorithm == LegacyAlgorithm
                    ? DeriveLegacy(password, salt, iterations, expected.Length)
                    : Derive(password, salt, iterations, expected.Length);
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
            if (!TryParse(encodedHash, out var algorithm, out var iterations, out _, out _)) return true;
            return algorithm != Algorithm || iterations != Iterations;
        }

        private static bool TryParse(string encodedHash, out string algorithm, out int iterations, out byte[] salt, out byte[] hash)
        {
            algorithm = null; iterations = 0; salt = null; hash = null;
            if (string.IsNullOrEmpty(encodedHash)) return false;

            var parts = encodedHash.Split('$');
            if (parts.Length != 4) return false;
            if (parts[0] != Algorithm && parts[0] != LegacyAlgorithm) return false;
            algorithm = parts[0];
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

        private static class NativeMethods
        {
            internal const int BCRYPT_ALG_HANDLE_HMAC_FLAG = 0x8;

            [DllImport("bcrypt.dll", CharSet = CharSet.Unicode)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            internal static extern int BCryptOpenAlgorithmProvider(out IntPtr phAlgorithm, string pszAlgId, string pszImplementation, int dwFlags);

            [DllImport("bcrypt.dll")]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            internal static extern int BCryptDeriveKeyPBKDF2(IntPtr hPrf, byte[] pbPassword, int cbPassword, byte[] pbSalt, int cbSalt,
                ulong cIterations, byte[] pbDerivedKey, int cbDerivedKey, int dwFlags);

            [DllImport("bcrypt.dll")]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            internal static extern int BCryptCloseAlgorithmProvider(IntPtr hAlgorithm, int dwFlags);
        }
    }
}
