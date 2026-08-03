using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Exceptions;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using ChequeAR.Infrastructure.EF;

namespace ChequeAR.Infrastructure.Repositories
{
    public class QuestionnaireResponseRepository : IQuestionnaireResponseRepository
    {
        private readonly ChequeARDbContext _ctx;
        public QuestionnaireResponseRepository(ChequeARDbContext ctx) { _ctx = ctx; }

        public Task<QuestionnaireResponse> GetAsync(Guid id) =>
            _ctx.QuestionnaireResponses.Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == id);

        public async Task<IReadOnlyList<QuestionnaireResponse>> SearchAsync(string patientHospitalId, Guid? questionnaireId, ResponseStatus? status, int skip, int take)
        {
            var q = _ctx.QuestionnaireResponses.AsQueryable();
            if (!string.IsNullOrEmpty(patientHospitalId)) q = q.Where(x => x.PatientHospitalId == patientHospitalId);
            if (questionnaireId.HasValue) q = q.Where(x => x.QuestionnaireId == questionnaireId.Value);
            if (status.HasValue) q = q.Where(x => x.Status == status.Value);
            return await q.OrderByDescending(x => x.AuthoredAt).Skip(skip).Take(take).ToListAsync();
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
                var stale = ex.Entries.Select(e => e.Entity).OfType<QuestionnaireResponse>().FirstOrDefault();
                var expected = stale?.ConcurrencyVersion ?? 0;
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
