using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Exceptions;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Security;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using ChequeAR.Infrastructure.EF;

namespace ChequeAR.Infrastructure.Repositories
{
    public class QuestionnaireResponseRepository : IQuestionnaireResponseRepository
    {
        private readonly ChequeARDbContext _ctx;
        public QuestionnaireResponseRepository(ChequeARDbContext ctx) { _ctx = ctx; }

        public async Task<IReadOnlyList<Guid>> FindQuestionnaireIdsByAttachmentAsync(Guid attachmentId)
        {
            return await (from i in _ctx.QuestionnaireResponseItems
                          join r in _ctx.QuestionnaireResponses on i.ResponseId equals r.Id
                          where i.AttachmentId == attachmentId
                          select r.QuestionnaireId).Distinct().ToListAsync();
        }

        public Task<QuestionnaireResponse> GetAsync(Guid id) =>
            _ctx.QuestionnaireResponses.Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == id);

        public async Task<IReadOnlyList<QuestionnaireResponse>> SearchAsync(string patientHospitalId, Guid? questionnaireId, ResponseStatus? status, int skip, int take, SpecialtyScope scope = null)
        {
            if (scope != null && !scope.IsUnrestricted && scope.IsEmpty) return new List<QuestionnaireResponse>();
            return await Filtered(patientHospitalId, questionnaireId, status, scope)
                .OrderByDescending(x => x.AuthoredAt).Skip(skip).Take(take).ToListAsync();
        }

        public async Task<int> CountAsync(string patientHospitalId = null, Guid? questionnaireId = null, ResponseStatus? status = null, SpecialtyScope scope = null)
        {
            if (scope != null && !scope.IsUnrestricted && scope.IsEmpty) return 0;
            return await Filtered(patientHospitalId, questionnaireId, status, scope).CountAsync();
        }

        /// <summary>Los filtros de búsqueda, compartidos por SearchAsync y CountAsync para que nunca diverjan.</summary>
        private IQueryable<QuestionnaireResponse> Filtered(string patientHospitalId, Guid? questionnaireId, ResponseStatus? status, SpecialtyScope scope)
        {
            var q = _ctx.QuestionnaireResponses.AsQueryable();
            if (!string.IsNullOrEmpty(patientHospitalId)) q = q.Where(x => x.PatientHospitalId == patientHospitalId);
            if (questionnaireId.HasValue) q = q.Where(x => x.QuestionnaireId == questionnaireId.Value);
            if (status.HasValue) q = q.Where(x => x.Status == status.Value);

            if (scope != null && !scope.IsUnrestricted)
            {
                var codes = scope.Codes.ToList();
                // Sin navegación a Questionnaire: subconsulta explícita. EF6 la traduce a
                // WHERE "QuestionnaireId" IN (SELECT "Id" FROM "Questionnaire" WHERE
                // "SpecialtyCode" IN (...)).
                q = q.Where(x => _ctx.Questionnaires.Any(qq => qq.Id == x.QuestionnaireId && codes.Contains(qq.SpecialtyCode)));
            }
            return q;
        }

        public async Task AddAsync(QuestionnaireResponse r)
        {
            _ctx.QuestionnaireResponses.Add(r);
            await _ctx.SaveChangesAsync();
        }

        public Task UpdateAsync(QuestionnaireResponse r) => SaveAsync();

        public async Task AddItemsAsync(IEnumerable<QuestionnaireResponseItem> items)
        {
            _ctx.QuestionnaireResponseItems.AddRange(items);
            await _ctx.SaveChangesAsync();
        }

        public Task<bool> HasResponsesAsync(Guid questionnaireId)
        {
            return _ctx.QuestionnaireResponses.AnyAsync(r => r.QuestionnaireId == questionnaireId);
        }

        public async Task SaveProgressAsync(QuestionnaireResponse r, ICollection<QuestionnaireResponseItem> replacementItems)
        {
            var linkIds = (replacementItems ?? new List<QuestionnaireResponseItem>())
                .Select(i => i.LinkId).Distinct().ToList();

            // Se opera contra el DbSet, no contra r.Items: la relación es requerida
            // (WillCascadeOnDelete(false)), así que sacar un item de la colección de
            // navegación sin borrarlo del set intentaría poner su FK en null y EF lo
            // rechazaría. Mismo criterio que QuestionnaireRepository.ReplaceStructureAsync.
            if (linkIds.Count > 0)
            {
                var stale = await _ctx.QuestionnaireResponseItems
                    .Where(i => i.ResponseId == r.Id && linkIds.Contains(i.LinkId)).ToListAsync();
                _ctx.QuestionnaireResponseItems.RemoveRange(stale);
            }
            _ctx.QuestionnaireResponseItems.AddRange(replacementItems ?? new List<QuestionnaireResponseItem>());

            // Cabecera (con el token de concurrencia) e items en el mismo SaveChanges: la
            // ficha nunca queda con items nuevos pero el ConcurrencyVersion/FhirJson viejo,
            // ni viceversa.
            await SaveAsync();
        }

        public async Task RetargetAsync(QuestionnaireResponse r, ICollection<string> keepLinkIds)
        {
            var keep = new HashSet<string>(keepLinkIds ?? new List<string>());
            // Se materializan y filtran en memoria: EF6 no traduce bien `!keep.Contains(...)`
            // sobre una colección local dentro del WHERE. Se borran los items cuyo linkId ya no
            // existe en la versión nueva (las respuestas descartadas del re-anclaje).
            var current = await _ctx.QuestionnaireResponseItems
                .Where(i => i.ResponseId == r.Id).ToListAsync();
            var drop = current.Where(i => !keep.Contains(i.LinkId)).ToList();
            if (drop.Count > 0) _ctx.QuestionnaireResponseItems.RemoveRange(drop);

            // Cabecera (nueva versión anclada + FhirJson + ConcurrencyVersion) y borrado de
            // items en el mismo SaveChanges, por el mismo motivo que SaveProgressAsync.
            await SaveAsync();
        }

        public void DiscardChanges()
        {
            foreach (var entry in _ctx.ChangeTracker.Entries().ToList())
                entry.State = EntityState.Detached;
        }

        public async Task ReplaceItemsAsync(QuestionnaireResponse r, ICollection<QuestionnaireResponseItem> items)
        {
            var current = await _ctx.QuestionnaireResponseItems.Where(i => i.ResponseId == r.Id).ToListAsync();
            _ctx.QuestionnaireResponseItems.RemoveRange(current);
            _ctx.QuestionnaireResponseItems.AddRange(items ?? new List<QuestionnaireResponseItem>());
            await SaveAsync();
        }

        // Traduce el choque de concurrencia de EF al lenguaje de la aplicación, igual que
        // QuestionnaireRepository.SaveAsync: salta cuando el UPDATE afecta 0 filas porque
        // el ConcurrencyVersion del WHERE ya no coincide (alguien más guardó/tomó el lock
        // en el medio).
        private async Task SaveAsync()
        {
            try
            {
                await _ctx.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                var entry = ex.Entries.FirstOrDefault(e => e.Entity is QuestionnaireResponse);
                var stale = entry?.Entity as QuestionnaireResponse;
                var expected = entry?.OriginalValues.GetValue<int>(nameof(QuestionnaireResponse.ConcurrencyVersion)) ?? 0;
                var actual = expected;
                if (stale != null)
                {
                    var fromDb = await _ctx.QuestionnaireResponses.AsNoTracking()
                        .FirstOrDefaultAsync(x => x.Id == stale.Id);
                    actual = fromDb?.ConcurrencyVersion ?? expected;
                }
                throw new ConcurrencyConflictException(expected, actual);
            }
        }
    }
}
