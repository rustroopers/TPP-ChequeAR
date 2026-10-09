using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Dtos;
using ChequeAR.Application.Exceptions;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using Moq;
using Xunit;

namespace ChequeAR.Tests.Application
{
    public class UserServiceRoleTests
    {
        private static User UserWithRole(string id, string role, bool active = true)
        {
            var u = new User { Id = id, DisplayName = id, IsActive = active };
            if (role != null) u.Roles.Add(new UserRole { UserId = id, Role = role });
            return u;
        }

        private static UserService Sut(Mock<IUserRepository> repo)
            => new UserService(repo.Object, TestHelpers.CatalogWith());

        // ---- SyncFromHospitalAsync: normalización a rol único ----

        [Fact]
        public async Task SyncFromHospitalAsync_VariosRoles_PersisteSoloElMasAlto()
        {
            var repo = new Mock<IUserRepository>();
            IReadOnlyList<string> capturedRoles = null;
            repo.Setup(r => r.SyncFromHospitalAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>()))
                .Callback<string, string, string, IReadOnlyList<string>, IReadOnlyList<string>>(
                    (id, name, email, roles, specs) => capturedRoles = roles)
                .Returns(Task.CompletedTask);

            var sut = Sut(repo);
            var result = await sut.SyncFromHospitalAsync(new UserInfo
            {
                Id = "u1", DisplayName = "U1",
                Roles = new List<string> { Role.Medico, Role.Jefe },
                Specialties = new List<string> { "cardio" }
            });

            Assert.Equal(new[] { Role.Jefe }, capturedRoles);
            Assert.Equal(new[] { Role.Jefe }, result.Roles);
        }

        [Fact]
        public async Task SyncFromHospitalAsync_RolHeredadoDesconocido_DegradaAMedico()
        {
            var repo = new Mock<IUserRepository>();
            IReadOnlyList<string> capturedRoles = null;
            repo.Setup(r => r.SyncFromHospitalAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>()))
                .Callback<string, string, string, IReadOnlyList<string>, IReadOnlyList<string>>(
                    (id, name, email, roles, specs) => capturedRoles = roles)
                .Returns(Task.CompletedTask);

            var sut = Sut(repo);
            await sut.SyncFromHospitalAsync(new UserInfo { Id = "u1", DisplayName = "U1", Roles = new List<string> { "Observador" } });

            Assert.Equal(new[] { Role.Medico }, capturedRoles);
        }

        [Fact]
        public async Task SyncFromHospitalAsync_RolesNulo_NoTocaRoles()
        {
            var repo = new Mock<IUserRepository>();
            IReadOnlyList<string> capturedRoles = new List<string> { "no-deberia-verse" };
            repo.Setup(r => r.SyncFromHospitalAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>()))
                .Callback<string, string, string, IReadOnlyList<string>, IReadOnlyList<string>>(
                    (id, name, email, roles, specs) => capturedRoles = roles)
                .Returns(Task.CompletedTask);

            var sut = Sut(repo);
            await sut.SyncFromHospitalAsync(new UserInfo { Id = "u1", DisplayName = "U1", Roles = null, Specialties = new List<string> { "cardio" } });

            Assert.Null(capturedRoles);
        }

        // ---- SetRoleAsync ----

        [Fact]
        public async Task SetRoleAsync_RolDesconocido_Lanza()
        {
            var repo = new Mock<IUserRepository>();
            repo.Setup(r => r.GetAsync("u1")).ReturnsAsync(UserWithRole("u1", Role.Medico));
            var sut = Sut(repo);

            await Assert.ThrowsAsync<ArgumentException>(() => sut.SetRoleAsync("u1", "SuperAdmin"));
        }

        [Fact]
        public async Task SetRoleAsync_UsuarioInexistente_Lanza()
        {
            var repo = new Mock<IUserRepository>();
            repo.Setup(r => r.GetAsync("fantasma")).ReturnsAsync((User)null);
            var sut = Sut(repo);

            await Assert.ThrowsAsync<NotFoundException>(() => sut.SetRoleAsync("fantasma", Role.Medico));
        }

        [Fact]
        public async Task SetRoleAsync_DeMedicoAJefe_NoRequiereValidacionDeAdmin()
        {
            var repo = new Mock<IUserRepository>();
            repo.Setup(r => r.GetAsync("u1")).ReturnsAsync(UserWithRole("u1", Role.Medico));
            var sut = Sut(repo);

            await sut.SetRoleAsync("u1", Role.Jefe);

            repo.Verify(r => r.SyncFromHospitalAsync("u1", It.IsAny<string>(), null,
                It.Is<IReadOnlyList<string>>(roles => roles.SequenceEqual(new[] { Role.Jefe })), null), Times.Once);
        }

        [Fact]
        public async Task SetRoleAsync_AdminSeQuitaAsiMismo_Lanza()
        {
            var repo = new Mock<IUserRepository>();
            repo.Setup(r => r.GetAsync("admin1")).ReturnsAsync(UserWithRole("admin1", Role.Administrador));
            var sut = Sut(repo);

            await Assert.ThrowsAsync<BusinessRuleException>(
                () => sut.SetRoleAsync("admin1", Role.Jefe, actingUserId: "admin1"));

            repo.Verify(r => r.SyncFromHospitalAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>()), Times.Never);
        }

        [Fact]
        public async Task SetRoleAsync_UltimoAdministradorActivo_Lanza()
        {
            var repo = new Mock<IUserRepository>();
            repo.Setup(r => r.GetAsync("admin1")).ReturnsAsync(UserWithRole("admin1", Role.Administrador));
            repo.Setup(r => r.CountAsync(null, Role.Administrador, null, true)).ReturnsAsync(1);
            var sut = Sut(repo);

            // otro usuario (no el propio admin) intenta degradarlo, pero es el único admin.
            await Assert.ThrowsAsync<BusinessRuleException>(
                () => sut.SetRoleAsync("admin1", Role.Jefe, actingUserId: "otro.admin"));
        }

        [Fact]
        public async Task SetRoleAsync_HayOtroAdministrador_Permite()
        {
            var repo = new Mock<IUserRepository>();
            repo.Setup(r => r.GetAsync("admin1")).ReturnsAsync(UserWithRole("admin1", Role.Administrador));
            repo.Setup(r => r.CountAsync(null, Role.Administrador, null, true)).ReturnsAsync(2);
            var sut = Sut(repo);

            await sut.SetRoleAsync("admin1", Role.Jefe, actingUserId: "otro.admin");

            repo.Verify(r => r.SyncFromHospitalAsync("admin1", It.IsAny<string>(), null,
                It.Is<IReadOnlyList<string>>(roles => roles.SequenceEqual(new[] { Role.Jefe })), null), Times.Once);
        }
    }
}
