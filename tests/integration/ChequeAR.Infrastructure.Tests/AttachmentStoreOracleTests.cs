using System;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using ChequeAR.Infrastructure.EF;
using ChequeAR.Infrastructure.Storage;
using Xunit;

namespace ChequeAR.Infrastructure.Tests
{
    [Trait("Requires", "Oracle")]
    public sealed class AttachmentStoreOracleTests : IDisposable
    {
        // Fechas del año 2000: el corte no alcanza a ningún adjunto real de la base de desarrollo.
        private static readonly DateTime Viejo = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private readonly string _root = Path.Combine(Path.GetTempPath(), "cq-adjuntos-" + Guid.NewGuid().ToString("N"));
        private readonly Guid _huerfano = Guid.NewGuid();
        private readonly Guid _usado = Guid.NewGuid();
        private Guid _ficha;

        static AttachmentStoreOracleTests()
        {
            using (new ChequeARDbContext()) { }
            Database.SetInitializer<ChequeARDbContext>(null);
        }

        [Fact]
        public async Task PurgeOrphans_BorraSoloLosQueNingunaFichaUsa()
        {
            using (var ctx = new ChequeARDbContext())
            {
                var version = await ctx.QuestionnaireVersions.AsNoTracking().FirstAsync();
                Directory.CreateDirectory(_root);
                foreach (var id in new[] { _huerfano, _usado })
                {
                    File.WriteAllText(Path.Combine(_root, id + ".png"), "x");
                    ctx.Attachments.Add(new Attachment
                    {
                        Id = id, OriginalFileName = "a.png", ContentType = "image/png", SizeBytes = 1,
                        StoragePath = id + ".png", UploadedBy = "test", UploadedAt = Viejo, Hash = "x"
                    });
                }
                _ficha = Guid.NewGuid();
                var r = new QuestionnaireResponse
                {
                    Id = _ficha, QuestionnaireId = version.QuestionnaireId, QuestionnaireVersionId = version.Id,
                    PatientHospitalId = "TEST-INFRA", Status = ResponseStatus.InProgress, AuthoredAt = DateTime.UtcNow,
                    LastSavedAt = DateTime.UtcNow, AuthorUserId = "test", ConcurrencyVersion = 1
                };
                r.Items.Add(new QuestionnaireResponseItem { Id = Guid.NewGuid(), ResponseId = _ficha, LinkId = "adj", AttachmentId = _usado });
                ctx.QuestionnaireResponses.Add(r);
                await ctx.SaveChangesAsync();
            }

            int borrados;
            using (var ctx = new ChequeARDbContext())
                borrados = await new FileShareAttachmentStore(_root, ctx).PurgeOrphansAsync(Viejo.AddDays(1));

            Assert.Equal(1, borrados);
            Assert.False(File.Exists(Path.Combine(_root, _huerfano + ".png")));
            Assert.True(File.Exists(Path.Combine(_root, _usado + ".png")));
            using (var ctx = new ChequeARDbContext())
            {
                Assert.Null(await ctx.Attachments.FindAsync(_huerfano));
                Assert.NotNull(await ctx.Attachments.FindAsync(_usado));
            }
        }

        public void Dispose()
        {
            using (var ctx = new ChequeARDbContext())
            {
                ctx.Database.ExecuteSqlCommand(@"DELETE FROM ""QuestionnaireResponseItem"" WHERE ""ResponseId"" = :p0", _ficha.ToByteArray());
                ctx.Database.ExecuteSqlCommand(@"DELETE FROM ""QuestionnaireResponse"" WHERE ""Id"" = :p0", _ficha.ToByteArray());
                foreach (var id in new[] { _huerfano, _usado })
                    ctx.Database.ExecuteSqlCommand(@"DELETE FROM ""Attachment"" WHERE ""Id"" = :p0", id.ToByteArray());
            }
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }
}
