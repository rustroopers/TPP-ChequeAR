using System;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Dtos;
using ChequeAR.Application.Exceptions;
using ChequeAR.Domain.Entities;
using ChequeAR.Infrastructure.EF;
using ChequeAR.Infrastructure.Repositories;
using Xunit;

namespace ChequeAR.Infrastructure.Tests
{
    [Trait("Requires", "Oracle")]
    public sealed class UserAdminEditOracleTests : IAsyncLifetime
    {
        private readonly string _id = "qa.edit." + Guid.NewGuid().ToString("N").Substring(0, 8);
        private static readonly DateTime UltimoIngreso = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        static UserAdminEditOracleTests()
        {
            using (new ChequeARDbContext()) { }
            Database.SetInitializer<ChequeARDbContext>(null);
        }

        public async Task InitializeAsync()
        {
            using (var ctx = new ChequeARDbContext())
            {
                var u = new User { Id = _id, DisplayName = "QA Edit", IsActive = true, LastLoginAt = UltimoIngreso, LastSyncAt = UltimoIngreso };
                u.Roles.Add(new UserRole { UserId = _id, Role = "Medico" });
                u.Specialties.Add(new UserSpecialty { UserId = _id, SpecialtyCode = "cardio" });
                ctx.Users.Add(u);
                await ctx.SaveChangesAsync();
            }
        }

        public async Task DisposeAsync()
        {
            using (var ctx = new ChequeARDbContext())
            {
                foreach (var tabla in new[] { "UserSpecialty", "UserRole", "RefreshToken" })
                    await ctx.Database.ExecuteSqlCommandAsync(
                        "DELETE FROM \"CHEQUEAR\".\"" + tabla + "\" WHERE \"UserId\" = :p0", _id);
                await ctx.Database.ExecuteSqlCommandAsync("DELETE FROM \"CHEQUEAR\".\"AppUser\" WHERE \"Id\" = :p0", _id);
            }
        }

        private async Task<User> Leer()
        {
            using (var ctx = new ChequeARDbContext())
                return await ctx.Users.AsNoTracking().Include(u => u.Roles).Include(u => u.Specialties).SingleAsync(u => u.Id == _id);
        }

        private AdminUserEdit Edicion(bool activo = true, string rol = "Jefe", int? revision = 0, params string[] especialidades)
            => new AdminUserEdit { UserId = _id, IsActive = activo, Role = rol, Specialties = especialidades, ExpectedRevision = revision };

        [Fact]
        public async Task GuardaTodo_SubeLaRevision_YNoTocaLasFechasDelLogin()
        {
            using (var ctx = new ChequeARDbContext())
                await new UserRepository(ctx).ApplyAdminEditAsync(Edicion(activo: false, especialidades: new[] { "neumo" }));

            var u = await Leer();
            Assert.False(u.IsActive);
            Assert.Equal("Jefe", u.Roles.Single().Role);
            Assert.Equal("neumo", u.Specialties.Single().SpecialtyCode);
            Assert.Equal(1, u.Revision);
            Assert.Equal(UltimoIngreso, u.LastLoginAt);
            Assert.Equal(UltimoIngreso, u.LastSyncAt);
            Assert.NotNull(u.SecurityStamp);
        }

        [Fact]
        public async Task SiFallaUnaParte_NoQuedaNadaGuardado()
        {
            using (var ctx = new ChequeARDbContext())
                await Assert.ThrowsAnyAsync<Exception>(() =>
                    new UserRepository(ctx).ApplyAdminEditAsync(Edicion(activo: false, especialidades: new[] { new string('x', 65) })));

            var u = await Leer();
            Assert.True(u.IsActive);
            Assert.Equal("Medico", u.Roles.Single().Role);
            Assert.Equal("cardio", u.Specialties.Single().SpecialtyCode);
            Assert.Equal(0, u.Revision);
        }

        [Fact]
        public async Task DosAdmins_ElSegundoRecibeConflicto()
        {
            using (var ctx = new ChequeARDbContext())
                await new UserRepository(ctx).ApplyAdminEditAsync(Edicion(rol: "Jefe", revision: 0, especialidades: new[] { "cardio" }));

            using (var ctx = new ChequeARDbContext())
            {
                var ex = await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
                    new UserRepository(ctx).ApplyAdminEditAsync(Edicion(rol: "Medico", revision: 0, especialidades: new[] { "neumo" })));
                Assert.Equal(1, ex.ActualVersion);
            }

            var u = await Leer();
            Assert.Equal("Jefe", u.Roles.Single().Role);
            Assert.Equal("cardio", u.Specialties.Single().SpecialtyCode);
        }

        [Fact]
        public async Task UsuarioInexistente_NotFound()
        {
            using (var ctx = new ChequeARDbContext())
                await Assert.ThrowsAsync<NotFoundException>(() =>
                    new UserRepository(ctx).ApplyAdminEditAsync(new AdminUserEdit { UserId = "qa.no.existe", Role = "Medico", ExpectedRevision = 0 }));
        }
    }
}
