using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
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

        public Task UpdateAsync(QuestionnaireResponse r) => _ctx.SaveChangesAsync();

        public async Task AddItemsAsync(IEnumerable<QuestionnaireResponseItem> items)
        {
            _ctx.QuestionnaireResponseItems.AddRange(items);
            await _ctx.SaveChangesAsync();
        }
    }
}
