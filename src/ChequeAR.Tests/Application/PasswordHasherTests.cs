using System;
using System.Text;
using ChequeAR.Application.Security;
using Xunit;

namespace ChequeAR.Tests.Application
{
    public class PasswordHasherTests
    {
        [Fact]
        public void Hash_ProduceFormatoConCuatroCampos()
        {
            var hash = PasswordHasher.Hash("Chequear.2026!");
            var parts = hash.Split('$');
            Assert.Equal(4, parts.Length);
            Assert.Equal("pbkdf2-sha256", parts[0]);
            Assert.Equal("600000", parts[1]);
        }

        // Vector de RFC 7914 §11 (PBKDF2-HMAC-SHA256, c = 1).
        [Fact]
        public void Verify_VectorConocidoSha256()
        {
            var esperado = new byte[]
            {
                0x55, 0xac, 0x04, 0x6e, 0x56, 0xe3, 0x08, 0x9f, 0xec, 0x16, 0x91, 0xc2, 0x25, 0x44, 0xb6, 0x05,
                0xf9, 0x41, 0x85, 0x21, 0x6d, 0xde, 0x04, 0x65, 0xe6, 0x8b, 0x9d, 0x57, 0xc2, 0x0d, 0xac, 0xbc,
            };
            var hash = "pbkdf2-sha256$1$" + Convert.ToBase64String(Encoding.ASCII.GetBytes("salt")) + "$" + Convert.ToBase64String(esperado);

            Assert.True(PasswordHasher.Verify("passwd", hash));
            Assert.False(PasswordHasher.Verify("password", hash));
        }

        [Fact]
        public void Verify_HashSha1Anterior_SigueValiendoYPideRehash()
        {
            var salt = new byte[16];
#pragma warning disable CA5379, CA5387 // formato de los hashes anteriores
            using (var kdf = new System.Security.Cryptography.Rfc2898DeriveBytes("vieja", salt, 1000))
#pragma warning restore CA5379, CA5387
            {
                var hash = "pbkdf2-sha1$1000$" + Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(kdf.GetBytes(32));

                Assert.True(PasswordHasher.Verify("vieja", hash));
                Assert.True(PasswordHasher.NeedsRehash(hash));
            }
        }

        [Fact]
        public void Hash_DosVecesLaMismaPassword_DaHashesDistintos()
        {
            var h1 = PasswordHasher.Hash("igual");
            var h2 = PasswordHasher.Hash("igual");
            Assert.NotEqual(h1, h2); // salt aleatorio
        }

        [Fact]
        public void Verify_PasswordCorrecta_DevuelveTrue()
        {
            var hash = PasswordHasher.Hash("mi-contraseña-123");
            Assert.True(PasswordHasher.Verify("mi-contraseña-123", hash));
        }

        [Fact]
        public void Verify_PasswordIncorrecta_DevuelveFalse()
        {
            var hash = PasswordHasher.Hash("correcta");
            Assert.False(PasswordHasher.Verify("incorrecta", hash));
        }

        [Fact]
        public void Verify_HashNulo_DevuelveFalseSinExcepcion()
        {
            Assert.False(PasswordHasher.Verify("cualquiera", null));
        }

        [Fact]
        public void Verify_HashVacio_DevuelveFalseSinExcepcion()
        {
            Assert.False(PasswordHasher.Verify("cualquiera", ""));
        }

        [Theory]
        [InlineData("nosoyunhash")]
        [InlineData("pbkdf2-sha1$abc$$")]
        [InlineData("pbkdf2-sha1$210000$noesbase64!!$noesbase64!!")]
        [InlineData("otro-algo$210000$AAAA$BBBB")]
        [InlineData("pbkdf2-sha1$0$AAAA$BBBB")]
        [InlineData("pbkdf2-sha1$-5$AAAA$BBBB")]
        public void Verify_FormatoCorrupto_DevuelveFalseSinExcepcion(string corrupto)
        {
            Assert.False(PasswordHasher.Verify("cualquiera", corrupto));
        }

        [Fact]
        public void Verify_EsCaseSensitive()
        {
            var hash = PasswordHasher.Hash("Contraseña");
            Assert.False(PasswordHasher.Verify("contraseña", hash));
        }

        [Fact]
        public void Verify_PasswordVacia_NoRompe()
        {
            var hash = PasswordHasher.Hash("algo");
            Assert.False(PasswordHasher.Verify("", hash));
        }

        [Fact]
        public void NeedsRehash_ConParametrosVigentes_DevuelveFalse()
        {
            var hash = PasswordHasher.Hash("algo");
            Assert.False(PasswordHasher.NeedsRehash(hash));
        }

        [Fact]
        public void NeedsRehash_FormatoInvalido_DevuelveTrue()
        {
            Assert.True(PasswordHasher.NeedsRehash("no-es-un-hash"));
        }

        [Fact]
        public void NeedsRehash_HashNulo_DevuelveTrue()
        {
            Assert.True(PasswordHasher.NeedsRehash(null));
        }
    }
}
