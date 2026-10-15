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
    /// Pasa al código de la opción las respuestas que guardaron el texto visible, contra la
    /// versión anclada de cada ficha, y regenera el recurso FHIR de las que cambian. No se
    /// puede hacer en una migración: hay que leer el JSON de cada versión.
    ///
    /// Corre desde el Seed en cada arranque. Es idempotente: un código no se vuelve a mapear,
    /// así que una vez migrado no toca nada.
    /// </summary>
    internal static class OptionCodeBackfill
    {
        public static int Run(ChequeARDbContext context)
        {
            // Join y no Contains: Oracle no acepta más de 1000 elementos en un IN.
            var rows = (from i in context.QuestionnaireResponseItems
                        where i.ValueCode != null
                        join r in context.QuestionnaireResponses on i.ResponseId equals r.Id
                        select new { i.ResponseId, i.LinkId, i.ValueCode, r.QuestionnaireId, r.QuestionnaireVersionId })
                .AsNoTracking().ToList();
            if (rows.Count == 0) return 0;

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
                .Select(x => x.ResponseId).Distinct().ToList();

            var migradas = 0;
            foreach (var id in stale)
            {
                // Una ficha que falla no frena a las demás.
                try
                {
                    var response = context.QuestionnaireResponses.Include(r => r.Items).Single(r => r.Id == id);
                    var structure = StructureOf(response.QuestionnaireId, response.QuestionnaireVersionId);
                    if (OptionCodes.Normalize(structure, response.Items) == 0) continue;
                    response.FhirJson = FhirQuestionnaireResponseBuilder.Build(
                        response, structure, FhirQuestionnaireResponseBuilder.ContainedPatient(response.FhirJson));
                    if (response.Status != ResponseStatus.InProgress)
                        context.AuditEvents.Add(AuditEventBuilder.For(
                            "update", "QuestionnaireResponse/" + id + "$option-codes", "sistema", category: "system"));
                    context.SaveChanges();
                    migradas++;
                }
                catch (Exception ex)
                {
                    Trace.TraceError("No se pudo pasar a código la ficha " + id + ": " + ex);
                    foreach (var entry in context.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged).ToList())
                        entry.State = EntityState.Detached;
                }
            }
            return migradas;
        }

        private class Versions
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
