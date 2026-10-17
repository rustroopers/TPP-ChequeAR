using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Infrastructure.EF;
using ChequeAR.Infrastructure.Repositories;
using Xunit;

namespace ChequeAR.Infrastructure.Tests
{
    [Trait("Requires", "Oracle")]
    public class UserRepositoryOracleTests
    {
        static UserRepositoryOracleTests()
        {
            using (new ChequeARDbContext()) { }
            Database.SetInitializer<ChequeARDbContext>(null);
        }

        private const string AdminsActivos =
            @"SELECT COUNT(*) FROM ""CHEQUEAR"".""UserRole"" r JOIN ""CHEQUEAR"".""AppUser"" u ON u.""Id"" = r.""UserId"" WHERE r.""Role"" = 'Administrador' AND u.""IsActive"" = 1";

        // Un cambio que deja al sistema sin administradores activos se revierte entero.
        [Fact]
        public async Task KeepingAnAdmin_SiNoQuedaNinguno_Revierte()
        {
            int antes;
            using (var ctx = new ChequeARDbContext())
                antes = (int)await ctx.Database.SqlQuery<decimal>(AdminsActivos).SingleAsync();
            Assert.True(antes > 0, "La base de desarrollo no tiene administradores activos.");

            bool aplicado;
            using (var ctx = new ChequeARDbContext())
            {
                aplicado = await new UserRepository(ctx).KeepingAnAdminAsync(() => ctx.Database.ExecuteSqlCommandAsync(
                    @"UPDATE ""CHEQUEAR"".""AppUser"" SET ""IsActive"" = 0 WHERE ""Id"" IN (SELECT ""UserId"" FROM ""CHEQUEAR"".""UserRole"" WHERE ""Role"" = 'Administrador')"));
            }

            using (var ctx = new ChequeARDbContext())
            {
                Assert.False(aplicado);
                Assert.Equal(antes, (int)await ctx.Database.SqlQuery<decimal>(AdminsActivos).SingleAsync());
            }
        }

        [Fact]
        public async Task KeepingAnAdmin_SiQueda_Confirma()
        {
            using (var ctx = new ChequeARDbContext())
            {
                var aplicado = await new UserRepository(ctx).KeepingAnAdminAsync(() => Task.CompletedTask);
                Assert.True(aplicado);
            }
        }
    }
}
