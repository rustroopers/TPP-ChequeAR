using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Dtos;
using ChequeAR.Application.Exceptions;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Security;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using Moq;
using Xunit;

namespace ChequeAR.Tests.Application
{
    public sealed class UserServiceAdminEditTests : IDisposable
    {
        private readonly List<AuditEvent> _eventos = new List<AuditEvent>();
        private readonly AuditDispatcher _dispatcher;
        private readonly Mock<IUserRepository> _repo = new Mock<IUserRepository>();
        private AdminUserEdit _guardado;

        public UserServiceAdminEditTests()
        {
            var auditRepo = new Mock<IAuditRepository>();
            auditRepo.Setup(r => r.AddManyAsync(It.IsAny<IEnumerable<AuditEvent>>()))
                .Callback<IEnumerable<AuditEvent>>(lote => _eventos.AddRange(lote))
                .Returns(Task.CompletedTask);
            _dispatcher = new AuditDispatcher(auditRepo.Object, flushIntervalMs: 600000);
            _repo.Setup(r => r.ApplyAdminEditAsync(It.IsAny<AdminUserEdit>()))
                .Callback<AdminUserEdit>(e => _guardado = e)
                .Returns(Task.CompletedTask);
        }

        public void Dispose() => _dispatcher.Dispose();

        private UserService Sut(SpecialtyCatalogService catalog = null)
            => new UserService(_repo.Object, catalog ?? TestHelpers.CatalogWith(), _dispatcher);

        private static User Usuario(string id = "m1", string role = Role.Medico, bool active = true, params string[] specialties)
        {
            var u = new User { Id = id, DisplayName = id.ToUpperInvariant(), IsActive = active, Revision = 3 };
            u.Roles.Add(new UserRole { UserId = id, Role = role });
            foreach (var s in specialties) u.Specialties.Add(new UserSpecialty { UserId = id, SpecialtyCode = s });
            return u;
        }

        private static SpecialtyCatalogService CatalogoConOncoInactiva() => TestHelpers.CatalogWith(
            new Specialty { Code = "general", Name = "General", IsActive = true, IsDefault = true },
            new Specialty { Code = "cardio", Name = "Cardiología", IsActive = true },
            new Specialty { Code = "onco", Name = "Oncología", IsActive = false });

        private void VerifyNingunaEscritura()
        {
            _repo.Verify(r => r.ApplyAdminEditAsync(It.IsAny<AdminUserEdit>()), Times.Never);
            _repo.Verify(r => r.SyncFromHospitalAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>()), Times.Never);
        }

        [Fact]
        public async Task Desactivar_YCambiarEspecialidades_GuardaTodoJunto()
        {
            _repo.Setup(r => r.GetAsync("m1")).ReturnsAsync(Usuario(specialties: new[] { "cardio", "onco" }));

            await Sut().UpdateByAdminAsync("m1", null, false, Role.Medico, new[] { "cardio" }, "admin1");

            Assert.False(_guardado.IsActive);
            Assert.Equal(new[] { "cardio" }, _guardado.Specialties);
            _repo.Verify(r => r.ApplyAdminEditAsync(It.IsAny<AdminUserEdit>()), Times.Once);
        }

        [Fact]
        public async Task Guardar_NoUsaElSyncDelLogin()
        {
            _repo.Setup(r => r.GetAsync("m1")).ReturnsAsync(Usuario());

            await Sut().UpdateByAdminAsync("m1", null, true, Role.Jefe, new[] { "cardio" }, "admin1");

            _repo.Verify(r => r.SyncFromHospitalAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>()), Times.Never);
        }

        [Fact]
        public async Task EspecialidadInactiva_QueYaTenia_SeConserva()
        {
            _repo.Setup(r => r.GetAsync("m1")).ReturnsAsync(Usuario(specialties: new[] { "cardio", "onco" }));

            await Sut(CatalogoConOncoInactiva()).UpdateByAdminAsync("m1", null, true, Role.Medico, new[] { "cardio" }, "admin1");

            Assert.Equal(new[] { "cardio", "onco" }, _guardado.Specialties.OrderBy(c => c));
        }

        [Fact]
        public async Task EspecialidadInactiva_Nueva_NoSeAsigna()
        {
            _repo.Setup(r => r.GetAsync("m1")).ReturnsAsync(Usuario());

            await Sut(CatalogoConOncoInactiva()).UpdateByAdminAsync("m1", null, true, Role.Medico, new[] { "onco", "inventada" }, "admin1");

            Assert.Empty(_guardado.Specialties);
        }

        [Fact]
        public async Task CatalogoVacio_NoGuardaNada()
        {
            _repo.Setup(r => r.GetAsync("m1")).ReturnsAsync(Usuario(specialties: new[] { "cardio" }));
            var vacio = new Mock<ISpecialtyRepository>();
            vacio.Setup(r => r.ListAsync(It.IsAny<bool>())).ReturnsAsync((IReadOnlyList<Specialty>)new List<Specialty>());

            await Assert.ThrowsAsync<BusinessRuleException>(() =>
                Sut(new SpecialtyCatalogService(() => vacio.Object)).UpdateByAdminAsync("m1", null, true, Role.Medico, new[] { "cardio" }, "admin1"));
            VerifyNingunaEscritura();
        }

        [Theory]
        [InlineData("Superusuario")]
        [InlineData(null)]
        [InlineData("")]
        public async Task RolInvalido_NoGuardaNada(string role)
        {
            _repo.Setup(r => r.GetAsync("m1")).ReturnsAsync(Usuario());

            var ex = await Assert.ThrowsAsync<ValidationFailedException>(() =>
                Sut().UpdateByAdminAsync("m1", null, true, role, new[] { "cardio" }, "admin1"));

            Assert.Equal("Rol desconocido.", ex.Message);
            VerifyNingunaEscritura();
        }

        [Fact]
        public async Task PasaLaRevisionEsperada()
        {
            _repo.Setup(r => r.GetAsync("m1")).ReturnsAsync(Usuario());

            await Sut().UpdateByAdminAsync("m1", null, true, Role.Medico, Array.Empty<string>(), "admin1", expectedRevision: 3);

            Assert.Equal(3, _guardado.ExpectedRevision);
        }

        [Fact]
        public async Task OtraPersonaGuardoAntes_PropagaElConflictoYNoAudita()
        {
            _repo.Setup(r => r.GetAsync("m1")).ReturnsAsync(Usuario());
            _repo.Setup(r => r.ApplyAdminEditAsync(It.IsAny<AdminUserEdit>()))
                .ThrowsAsync(new ConcurrencyConflictException(3, 4, "Otra persona modificó este usuario."));

            await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
                Sut().UpdateByAdminAsync("m1", null, true, Role.Jefe, Array.Empty<string>(), "admin1", expectedRevision: 3));
            await _dispatcher.FlushAsync();

            Assert.Empty(_eventos);
        }

        [Fact]
        public async Task DegradarAdmin_SeEscribeDentroDeKeepingAnAdmin()
        {
            _repo.Setup(r => r.GetAsync("a2")).ReturnsAsync(Usuario("a2", Role.Administrador));
            _repo.Setup(r => r.KeepingAnAdminAsync(It.IsAny<Func<Task>>())).Returns<Func<Task>>(async cambio => { await cambio(); return true; });

            await Sut().UpdateByAdminAsync("a2", null, true, Role.Jefe, Array.Empty<string>(), "admin1");

            _repo.Verify(r => r.KeepingAnAdminAsync(It.IsAny<Func<Task>>()), Times.Once);
            Assert.Equal(Role.Jefe, _guardado.Role);
        }

        [Fact]
        public async Task DegradarAlUltimoAdmin_SeRechaza()
        {
            _repo.Setup(r => r.GetAsync("a2")).ReturnsAsync(Usuario("a2", Role.Administrador));
            _repo.Setup(r => r.KeepingAnAdminAsync(It.IsAny<Func<Task>>())).ReturnsAsync(false);

            await Assert.ThrowsAsync<BusinessRuleException>(() =>
                Sut().UpdateByAdminAsync("a2", null, true, Role.Jefe, Array.Empty<string>(), "admin1"));
        }

        [Fact]
        public async Task AdminSeQuitaElRol_SeRechazaSinEscribir()
        {
            _repo.Setup(r => r.GetAsync("admin1")).ReturnsAsync(Usuario("admin1", Role.Administrador));

            await Assert.ThrowsAsync<BusinessRuleException>(() =>
                Sut().UpdateByAdminAsync("admin1", null, true, Role.Jefe, Array.Empty<string>(), "admin1"));
            VerifyNingunaEscritura();
        }

        [Fact]
        public async Task AuditaSoloLoQueCambio()
        {
            var antes = Usuario();
            var despues = Usuario(role: Role.Jefe, specialties: new[] { "cardio" });
            _repo.SetupSequence(r => r.GetAsync("m1")).ReturnsAsync(antes).ReturnsAsync(despues);

            await Sut().UpdateByAdminAsync("m1", null, true, Role.Jefe, new[] { "cardio" }, "admin1");
            await _dispatcher.FlushAsync();

            var evt = Assert.Single(_eventos);
            Assert.Equal("AppUser/m1", evt.EntityRef);
            Assert.Equal("admin1", evt.AgentUserId);
            var cambios = AuditChangeSet.Read(evt.FhirJson).Changes;
            Assert.Equal(new[] { "Rol", "Especialidades" }, cambios.Select(c => c.Field));
            Assert.Equal(Role.Medico, cambios[0].Before);
            Assert.Equal(Role.Jefe, cambios[0].After);
        }

        [Fact]
        public async Task SinCambios_NoAudita()
        {
            _repo.Setup(r => r.GetAsync("m1")).ReturnsAsync(Usuario());

            await Sut().UpdateByAdminAsync("m1", null, true, Role.Medico, Array.Empty<string>(), "admin1");
            await _dispatcher.FlushAsync();

            Assert.Empty(_eventos);
        }

        [Fact]
        public async Task Basicos_CamposNulos_QuedanComoEstaban()
        {
            var u = Usuario();
            u.Email = "m1@hospital";
            _repo.Setup(r => r.GetAsync("m1")).ReturnsAsync(u);
            User escrito = null;
            _repo.Setup(r => r.UpsertAsync(It.IsAny<User>())).Callback<User>(x => escrito = x).Returns(Task.CompletedTask);

            await Sut().UpdateBasicsAsync("m1", null, "nuevo@hospital", null, "admin1");

            Assert.Equal("M1", escrito.DisplayName);
            Assert.Equal("nuevo@hospital", escrito.Email);
            Assert.True(escrito.IsActive);
        }

        [Fact]
        public async Task Basicos_UsuarioInexistente_NoCrea()
        {
            _repo.Setup(r => r.GetAsync("fantasma")).ReturnsAsync((User)null);

            await Assert.ThrowsAsync<NotFoundException>(() =>
                Sut().UpdateBasicsAsync("fantasma", "X", null, true, "admin1"));
            _repo.Verify(r => r.UpsertAsync(It.IsAny<User>()), Times.Never);
        }

        [Fact]
        public async Task CambioDeClave_ConLaCuentaBloqueada_NoVerificaNiSuma()
        {
            var u = Usuario();
            u.PasswordHash = PasswordHasher.Hash("actual-123456");
            u.LockoutUntil = DateTime.UtcNow.AddMinutes(5);
            _repo.Setup(r => r.GetAsync("m1")).ReturnsAsync(u);

            var error = await Sut().ChangePasswordAsync("m1", "actual-123456", "nueva-clave-larga");

            Assert.NotNull(error);
            _repo.Verify(r => r.IncrementFailedLoginAsync(It.IsAny<string>()), Times.Never);
            _repo.Verify(r => r.SetPasswordHashAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }
    }
}
