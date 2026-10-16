using System;
using System.Data.Entity;
using System.Diagnostics;
using System.Linq;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using ChequeAR.Infrastructure.EF;

namespace ChequeAR.Infrastructure.Migrations
{
    /// <summary>
    /// Publica la versión 1 de las variables que existían antes del versionado y ata a ella las
    /// preguntas que tienen esa misma definición. Las que no coinciden (se armaron con una
    /// definición anterior, que no quedó guardada) quedan sin versión y el editor las marca
    /// como desactualizadas.
    ///
    /// Corre una sola vez: con alguna versión ya publicada no hace nada. Las variables nuevas
    /// se publican desde la pantalla, no acá.
    /// </summary>
    internal static class VariableVersionBackfill
    {
        public static int Run(ChequeARDbContext context)
        {
            if (context.ClinicalVariableVersions.Any()) return 0;

            var ids = context.ClinicalVariables.Where(v => v.CurrentVersionId == null).Select(v => v.Id).ToList();
            var publicadas = 0;
            foreach (var id in ids)
            {
                try
                {
                    var v = context.ClinicalVariables.Include(x => x.Options).Single(x => x.Id == id);
                    var definition = PublishInitial(context, v, "sistema");

                    var preguntas = context.Questions.Include(q => q.Options).Where(q => q.VariableId == id).ToList();
                    foreach (var q in preguntas)
                    {
                        var igual = definition.Matches(q);
                        q.VariableVersionId = igual ? v.CurrentVersionId : null;
                        q.VariableRevision = igual ? 1 : 0;
                    }
                    context.SaveChanges();
                    publicadas++;
                }
                catch (Exception ex)
                {
                    Trace.TraceError("No se pudo versionar la variable " + id + ": " + ex);
                    foreach (var entry in context.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged).ToList())
                        entry.State = EntityState.Detached;
                }
            }
            return publicadas;
        }

        /// <summary>Agrega la versión 1 con la definición actual y la deja vigente. No guarda.</summary>
        public static VariableDefinition PublishInitial(ChequeARDbContext context, ClinicalVariable v, string userId)
        {
            var definition = VariableDefinition.From(v);
            var version = new ClinicalVariableVersion
            {
                Id = Guid.NewGuid(),
                VariableId = v.Id,
                VersionNumber = 1,
                SourceRevision = v.Revision,
                DefinitionJson = definition.ToJson(),
                PublishedBy = userId,
                PublishedAt = DateTime.UtcNow,
                ChangeNote = "Versión inicial"
            };
            context.ClinicalVariableVersions.Add(version);
            v.CurrentVersionId = version.Id;
            return definition;
        }
    }
}
