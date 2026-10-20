using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Dtos;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Security;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace ChequeAR.Tests.Application
{
    public sealed class UserServiceSecurityTests : IDisposable
    {
        private readonly List<AuditEvent> _eventos = new List<AuditEvent>();
        private readonly AuditDispatcher _dispatcher;
        private readonly Mock<IUserRepository> _repo = new Mock<IUserRepository>();
        private readonly UserService _sut;

        public UserServiceSecurityTests()
        {
            var auditRepo = new Mock<IAuditRepository>();
            auditRepo.Setup(r => r.AddManyAsync(It.IsAny<IEnumerable<AuditEvent>>()))
                .Callback<IEnumerable<AuditEvent>>(lote => _eventos.AddRange(lote))
                .Returns(Task.CompletedTask);
            _dispatcher = new AuditDispatcher(auditRepo.Object, flushIntervalMs: 600000);
            _sut = new UserService(_repo.Object, TestHelpers.CatalogWith(), _dispatcher);
        }

        public void Dispose() => _dispatcher.Dispose();

        private static User Usuario(string password = "actual-123456", string role = Role.Medico)
        {
            var u = new User { Id = "m1", DisplayName = "M1", IsActive = true, PasswordHash = PasswordHasher.Hash(password) };
            u.Roles.Add(new UserRole { UserId = "m1", Role = role });
            return u;
        }

        [Fact]
        public async Task LoginFallido_UsuarioInexistente_SeAuditaSinElTexto()
        {
            await _sut.LoginFailedAsync("pegue-la-clave-aca");
            await _dispatcher.FlushAsync();

            var evt = Assert.Single(_eventos);
            Assert.Equal("AppUser/(inexistente)$login-failed", evt.EntityRef);
            Assert.Equal("4", evt.Outcome);
            Assert.Equal(UserService.AuditCategory, evt.Category);
        }

        [Fact]
        public async Task LoginYLogout_SeAuditan()
        {
            _sut.LoginSucceeded("m1");
            _sut.LoggedOut("m1");
            await _dispatcher.FlushAsync();

            Assert.Equal(new[] { "AppUser/m1$login", "AppUser/m1$logout" }, _eventos.Select(e => e.EntityRef));
            Assert.All(_eventos, e => Assert.Equal("m1", e.AgentUserId));
        }

        [Fact]
        public async Task ChangePassword_ActualIncorrecta_CuentaComoFallo()
        {
            _repo.Setup(r => r.GetAsync("m1")).ReturnsAsync(Usuario());
            _repo.Setup(r => r.IncrementFailedLoginAsync("m1")).ReturnsAsync(1);

            var error = await _sut.ChangePasswordAsync("m1", "otra", "nueva-clave-larga");

            Assert.NotNull(error);
            _repo.Verify(r => r.IncrementFailedLoginAsync("m1"), Times.Once);
            _repo.Verify(r => r.SetPasswordHashAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Theory]
        [InlineData("corta")]
        [InlineData("actual-123456")]
        [InlineData("M1        ")]
        public async Task ChangePassword_NuevaInvalida_NoSeGuarda(string nueva)
        {
            _repo.Setup(r => r.GetAsync("m1")).ReturnsAsync(Usuario());

            Assert.NotNull(await _sut.ChangePasswordAsync("m1", "actual-123456", nueva));
            _repo.Verify(r => r.SetPasswordHashAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ChangePassword_Valida_GuardaRotaElSelloYAudita()
        {
            _repo.Setup(r => r.GetAsync("m1")).ReturnsAsync(Usuario());
            string guardado = null;
            _repo.Setup(r => r.SetPasswordHashAsync("m1", It.IsAny<string>()))
                 .Callback<string, string>((_, h) => guardado = h).Returns(Task.CompletedTask);

            Assert.Null(await _sut.ChangePasswordAsync("m1", "actual-123456", "nueva-clave-larga"));
            await _dispatcher.FlushAsync();

            Assert.True(PasswordHasher.Verify("nueva-clave-larga", guardado));
            _repo.Verify(r => r.RotateSecurityStampAsync("m1"), Times.Once);
            Assert.Equal("AppUser/m1$password", Assert.Single(_eventos).EntityRef);
        }
    }
}
