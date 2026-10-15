using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Diagnostics;
using System.Linq;
using ChequeAR.Application.Fhir;
using ChequeAR.Application.Services;
using ChequeAR.Application.Validation;
using ChequeAR.Domain.Enums;
using ChequeAR.Infrastructure.EF;

namespace ChequeAR.Infrastructure.Migrations
{
    /// <summary>
    /// Pone al día las fichas guardadas antes de dos cambios, contra la versión anclada de cada una:
    ///  - las respuestas de opción que guardaron el texto visible pasan al código de la opción;
    ///  - el recurso FHIR guardado con el formato anterior (plano, sin perfil ni tipos de R5) se
    ///    regenera.
    /// No se puede hacer en una migración: hay que leer el JSON de cada versión.
    ///
    /// Corre desde el Seed en cada arranque. Es idempotente: un código no se vuelve a mapear y un
    /// recurso regenerado ya declara el perfil, así que una vez migrado no toca nada.
    /// </summary>
    internal static class ResponseBackfill
    {
        public static int Run(ChequeARDbContext context)
        {
            // Join y no Contains: Oracle no acepta más de 1000 elementos en un IN.
            var rows = (from i in context.QuestionnaireResponseItems
                        where i.ValueCode != null
                        join r in context.QuestionnaireResponses on i.ResponseId equals r.Id
                        select new { i.ResponseId, i.LinkId, i.ValueCode, r.QuestionnaireId, r.QuestionnaireVersionId })
                .AsNoTracking().ToList();

            var formatoViejo = context.QuestionnaireResponses.AsNoTracking()
                .Where(r => r.FhirJson != null && !r.FhirJson.Contains(FhirQuestionnaireResponseBuilder.ProfileUrl))
                .Select(r => r.Id).ToList();
            if (rows.Count == 0 && formatoViejo.Count == 0) return 0;

            var cache = new Dictionary<Guid, Versions>();
            RenderableQuestionnaire StructureOf(Guid questionnaireId, Guid versionId)
            {
                if (!cache.TryGetValue(questionnaireId, out var versions))
                    cache[questionnaireId] = versions = Versions.Load(context, questionnaireId);
                return versions.Pinned(versionId);
            }

            var stale = rows
                .Where(x =>
                {
                    var question = StructureOf(x.QuestionnaireId, x.QuestionnaireVersionId)?
                        .Groups.SelectMany(g => g.Questions).FirstOrDefault(q => q.LinkId == x.LinkId);
                    var option = question?.FindOption(x.ValueCode);
                    return option != null && option.Code != x.ValueCode;
                })
                .Select(x => x.ResponseId).ToList();
            var pendientes = stale.Concat(formatoViejo).Distinct().ToList();
            var regenerar = new HashSet<Guid>(formatoViejo);

            var migradas = 0;
            foreach (var id in pendientes)
            {
                // Una ficha que falla no frena a las demás.
                try
                {
                    var response = context.QuestionnaireResponses.Include(r => r.Items).Single(r => r.Id == id);
                    var structure = StructureOf(response.QuestionnaireId, response.QuestionnaireVersionId);
                    var cambiaron = OptionCodes.Normalize(structure, response.Items) > 0;
                    if (!cambiaron && !regenerar.Contains(id)) continue;
                    response.FhirJson = FhirQuestionnaireResponseBuilder.Build(
                        response, structure, FhirQuestionnaireResponseBuilder.ContainedPatient(response.FhirJson));
                    // Sólo se audita si cambió un dato; regenerar el formato no cambia la ficha.
                    if (cambiaron && response.Status != ResponseStatus.InProgress)
                        context.AuditEvents.Add(AuditEventBuilder.For(
                            "update", "QuestionnaireResponse/" + id + "$option-codes", "sistema", category: "system"));
                    context.SaveChanges();
                    migradas++;
                }
                catch (Exception ex)
                {
                    Trace.TraceError("No se pudo poner al día la ficha " + id + ": " + ex);
                    foreach (var entry in context.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged).ToList())
                        entry.State = EntityState.Detached;
                }
            }
            return migradas;
        }

        private sealed class Versions
        {
            private Dictionary<Guid, RenderableQuestionnaire> _byId;
            private RenderableQuestionnaire _current;

            public static Versions Load(ChequeARDbContext context, Guid questionnaireId)
            {
                var all = context.QuestionnaireVersions.AsNoTracking()
                    .Where(v => v.QuestionnaireId == questionnaireId).ToList();
                var currentId = context.Questionnaires.AsNoTracking()
                    .Where(q => q.Id == questionnaireId).Select(q => q.CurrentVersionId).FirstOrDefault();
                // Misma regla que Questionnaire.CurrentVersion: la apuntada o la más alta.
                var current = all.FirstOrDefault(v => v.Id == currentId)
                              ?? all.OrderByDescending(v => v.VersionNumber).FirstOrDefault();

                var byId = all.ToDictionary(v => v.Id, v => FhirQuestionnaireReader.Read(v.FhirJson));
                return new Versions
                {
                    _byId = byId,
                    _current = current == null ? null : byId[current.Id]
                };
            }

            /// <summary>La anclada; las fichas anteriores al anclaje caen a la vigente, igual que el servicio.</summary>
            public RenderableQuestionnaire Pinned(Guid versionId)
                => _byId.TryGetValue(versionId, out var pinned) ? pinned : _current;
        }
    }
}
