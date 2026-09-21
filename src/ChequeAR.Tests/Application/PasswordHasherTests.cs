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
            Assert.Equal("pbkdf2-sha1", parts[0]);
            Assert.Equal("210000", parts[1]);
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
