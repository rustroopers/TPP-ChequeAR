using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Infrastructure.EF;
using ChequeAR.Infrastructure.Repositories;
using Xunit;

namespace ChequeAR.Infrastructure.Tests
{
    [Trait("Requires", "Oracle")]
    public class QuestionnaireRepositoryOracleTests
    {
        static QuestionnaireRepositoryOracleTests()
        {
            using (new ChequeARDbContext()) { }
            Database.SetInitializer<ChequeARDbContext>(null);
        }

        [Fact]
        public async Task SearchAsync_ConEspecialidades_FiltraYPaginaEnLaBase()
        {
            using (var ctx = new ChequeARDbContext())
            {
                var codigos = await ctx.Questionnaires.Where(q => !q.IsDeleted).Select(q => q.SpecialtyCode).Distinct().ToListAsync();
                Assert.True(codigos.Count > 0, "La base de desarrollo no tiene cuestionarios.");
                var codigo = codigos[0];
                var una = new[] { codigo };
                var esperado = await ctx.Questionnaires.CountAsync(q => !q.IsDeleted && q.SpecialtyCode == codigo);
                var repo = new QuestionnaireRepository(ctx);

                var total = await repo.CountAsync(null, null, null, una);
                var pagina = await repo.SearchAsync(null, null, null, una, 0, 1);

                Assert.Equal(esperado, total);
                Assert.Single(pagina);
                Assert.All(pagina, q => Assert.Equal(codigo, q.SpecialtyCode));
                Assert.Equal(0, await repo.CountAsync(null, null, null, new string[0]));
            }
        }

        [Fact]
        public async Task GetHeadersAsync_SinEstructura()
        {
            using (var ctx = new ChequeARDbContext())
            {
                var id = await ctx.Questionnaires.Where(q => q.Questions.Any()).Select(q => q.Id).FirstAsync();

                var cabeceras = await new QuestionnaireRepository(ctx).GetHeadersAsync(new[] { id, id });

                var q1 = Assert.Single(cabeceras);
                Assert.False(string.IsNullOrEmpty(q1.Title));
                Assert.Empty(q1.Questions);
            }
        }
    }
}
