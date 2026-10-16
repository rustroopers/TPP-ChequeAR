using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Exceptions;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using ChequeAR.Infrastructure.EF;
using ChequeAR.Infrastructure.Repositories;
using Xunit;

namespace ChequeAR.Infrastructure.Tests
{
    /// <summary>
    /// Contra el Oracle de desarrollo: lo que los tests de Application no pueden ver porque usan
    /// un repositorio en memoria. Cada test crea su ficha y la borra al terminar.
    /// </summary>
    [Trait("Requires", "Oracle")]
    public sealed class ResponseRepositoryOracleTests : IDisposable
    {
        private readonly List<Guid> _creadas = new List<Guid>();

        static ResponseRepositoryOracleTests()
        {
            // El constructor estático del contexto registra las migraciones automáticas: se
            // dispara primero y después se apagan, para no correr el Seed en cada test.
            using (new ChequeARDbContext()) { }
            Database.SetInitializer<ChequeARDbContext>(null);
        }

        [Fact]
        public async Task DosContextos_ElSegundoChocaPorConcurrencia()
        {
            var id = await NuevaFichaAsync(ResponseStatus.InProgress);
            using (var ctx1 = new ChequeARDbContext())
            using (var ctx2 = new ChequeARDbContext())
            {
                var repo1 = new QuestionnaireResponseRepository(ctx1);
                var repo2 = new QuestionnaireResponseRepository(ctx2);
                var a = await repo1.GetAsync(id);
                var b = await repo2.GetAsync(id);

                a.ConcurrencyVersion++;
                a.LastSavedAt = DateTime.UtcNow;
                await repo1.UpdateAsync(a);

                b.ConcurrencyVersion++;
                b.LastSavedAt = DateTime.UtcNow;
                var ex = await Assert.ThrowsAsync<ConcurrencyConflictException>(() => repo2.UpdateAsync(b));
                Assert.Equal(a.ConcurrencyVersion, ex.ActualVersion);
            }
        }

        // NUMBER(18,2) redondeaba en silencio: 2,345678 quedaba en 2,35.
        [Fact]
        public async Task Decimal_SeisDecimales_IdaYVueltaYEnLaVista()
        {
            var id = await NuevaFichaAsync(ResponseStatus.Completed,
                new QuestionnaireResponseItem { LinkId = "peso", ValueDecimal = 2.345678m },
                new QuestionnaireResponseItem { LinkId = "dosis", ValueDecimal = 0.5m });

            using (var ctx = new ChequeARDbContext())
            {
                var items = await ctx.QuestionnaireResponseItems.Where(i => i.ResponseId == id).ToListAsync();
                Assert.Equal(2.345678m, items.Single(i => i.LinkId == "peso").ValueDecimal);

                var textos = await ctx.Database.SqlQuery<string>(
                    @"SELECT ""ValueText"" FROM ""vw_FlatResponses"" WHERE ""ResponseId"" = :p0 ORDER BY ""LinkId""", id.ToByteArray())
                    .ToListAsync();
                Assert.Equal(new[] { "0.5", "2.345678" }, textos);
            }
        }

        [Fact]
        public async Task Texto_DosMilCaracteresConAcentos_SeGuarda()
        {
            var texto = new string('á', 2000);
            var id = await NuevaFichaAsync(ResponseStatus.InProgress,
                new QuestionnaireResponseItem { LinkId = "obs", ValueString = texto });

            using (var ctx = new ChequeARDbContext())
            {
                var item = await ctx.QuestionnaireResponseItems.SingleAsync(i => i.ResponseId == id);
                Assert.Equal(texto, item.ValueString);
            }
        }

        private async Task<Guid> NuevaFichaAsync(ResponseStatus status, params QuestionnaireResponseItem[] items)
        {
            using (var ctx = new ChequeARDbContext())
            {
                var version = await ctx.QuestionnaireVersions.AsNoTracking().FirstOrDefaultAsync();
                Assert.True(version != null, "La base de desarrollo no tiene ninguna versión publicada.");
                var r = new QuestionnaireResponse
                {
                    Id = Guid.NewGuid(),
                    QuestionnaireId = version.QuestionnaireId,
                    QuestionnaireVersionId = version.Id,
                    PatientHospitalId = "TEST-INFRA",
                    Status = status,
                    AuthoredAt = DateTime.UtcNow,
                    LastSavedAt = DateTime.UtcNow,
                    AuthorUserId = "test",
                    ConcurrencyVersion = 1
                };
                foreach (var it in items)
                {
                    it.Id = Guid.NewGuid();
                    it.ResponseId = r.Id;
                    r.Items.Add(it);
                }
                _creadas.Add(r.Id);
                await new QuestionnaireResponseRepository(ctx).AddAsync(r);
                return r.Id;
            }
        }

        public void Dispose()
        {
            using (var ctx = new ChequeARDbContext())
            {
                foreach (var id in _creadas)
                {
                    ctx.Database.ExecuteSqlCommand(@"DELETE FROM ""QuestionnaireResponseItem"" WHERE ""ResponseId"" = :p0", id.ToByteArray());
                    ctx.Database.ExecuteSqlCommand(@"DELETE FROM ""QuestionnaireResponse"" WHERE ""Id"" = :p0", id.ToByteArray());
                }
            }
        }
    }
}
