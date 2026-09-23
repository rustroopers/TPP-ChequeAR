using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Security;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using Moq;
using Xunit;

namespace ChequeAR.Tests.Application
{
    public class LocalCredentialAuthenticatorTests
    {
        private static User ActiveUser(string id, string password, bool active = true)
        {
            var u = new User { Id = id, DisplayName = "Usuario " + id, IsActive = active, PasswordHash = PasswordHasher.Hash(password) };
            u.Roles.Add(new UserRole { UserId = id, Role = Role.Medico });
            u.Specialties.Add(new UserSpecialty { UserId = id, SpecialtyCode = "cardio" });
            return u;
        }

        [Fact]
        public async Task Login_UsuarioInexistente_DevuelveNull()
        {
            var repo = new Mock<IUserRepository>();
            repo.Setup(r => r.GetAsync("fantasma")).ReturnsAsync((User)null);
            var sut = new LocalCredentialAuthenticator(repo.Object);

            var result = await sut.LoginAsync("fantasma", "cualquiera");

            Assert.Null(result);
        }

        [Fact]
        public async Task Login_UsuarioInactivo_DevuelveNull()
        {
            var user = ActiveUser("u1", "correcta", active: false);
            var repo = new Mock<IUserRepository>();
            repo.Setup(r => r.GetAsync("u1")).ReturnsAsync(user);
            var sut = new LocalCredentialAuthenticator(repo.Object);

            var result = await sut.LoginAsync("u1", "correcta");

            Assert.Null(result);
        }

        [Fact]
        public async Task Login_SinPasswordHash_DevuelveNull()
        {
            var user = new User { Id = "u1", DisplayName = "U1", IsActive = true, PasswordHash = null };
            var repo = new Mock<IUserRepository>();
            repo.Setup(r => r.GetAsync("u1")).ReturnsAsync(user);
            var sut = new LocalCredentialAuthenticator(repo.Object);

            var result = await sut.LoginAsync("u1", "cualquiera");

            Assert.Null(result);
        }

        [Fact]
        public async Task Login_PasswordIncorrecta_DevuelveNull()
        {
            var user = ActiveUser("u1", "correcta");
            var repo = new Mock<IUserRepository>();
            repo.Setup(r => r.GetAsync("u1")).ReturnsAsync(user);
            var sut = new LocalCredentialAuthenticator(repo.Object);

            var result = await sut.LoginAsync("u1", "incorrecta");

            Assert.Null(result);
        }

        [Fact]
        public async Task Login_Ok_DevuelveRolesYEspecialidadesDelEspejo()
        {
            var user = ActiveUser("med.cardio", "Chequear.2026!");
            var repo = new Mock<IUserRepository>();
            repo.Setup(r => r.GetAsync("med.cardio")).ReturnsAsync(user);
            var sut = new LocalCredentialAuthenticator(repo.Object);

            var result = await sut.LoginAsync("med.cardio", "Chequear.2026!");

            Assert.NotNull(result);
            Assert.Equal("med.cardio", result.Id);
            Assert.Contains(Role.Medico, result.Roles);
            Assert.Contains("cardio", result.Specialties);
        }

        [Fact]
        public async Task Login_UsernameConEspacios_SeNormaliza()
        {
            var user = ActiveUser("med.cardio", "Chequear.2026!");
            var repo = new Mock<IUserRepository>();
            repo.Setup(r => r.GetAsync("med.cardio")).ReturnsAsync(user);
            var sut = new LocalCredentialAuthenticator(repo.Object);

            var result = await sut.LoginAsync("  med.cardio  ", "Chequear.2026!");

            Assert.NotNull(result);
        }

        [Fact]
        public async Task Login_UsernameVacio_DevuelveNullSinTocarElRepo()
        {
            var repo = new Mock<IUserRepository>();
            var sut = new LocalCredentialAuthenticator(repo.Object);

            var result = await sut.LoginAsync("   ", "cualquiera");

            Assert.Null(result);
            repo.Verify(r => r.GetAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Login_PasswordNula_DevuelveNullSinExcepcion()
        {
            var user = ActiveUser("u1", "correcta");
            var repo = new Mock<IUserRepository>();
            repo.Setup(r => r.GetAsync("u1")).ReturnsAsync(user);
            var sut = new LocalCredentialAuthenticator(repo.Object);

            var result = await sut.LoginAsync("u1", null);

            Assert.Null(result);
        }
    }
}
