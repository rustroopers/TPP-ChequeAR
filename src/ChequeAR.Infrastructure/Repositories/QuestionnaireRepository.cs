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
    public class QuestionnaireRepository : IQuestionnaireRepository
    {
        private readonly ChequeARDbContext _ctx;

        public QuestionnaireRepository(ChequeARDbContext ctx) { _ctx = ctx; }

        public Task<Questionnaire> GetAsync(Guid id)
        {
            return _ctx.Questionnaires
                .Include(q => q.Versions)
                .Include(q => q.Questions)
                .FirstOrDefaultAsync(q => q.Id == id && !q.IsDeleted);
        }

        public async Task<IReadOnlyList<Questionnaire>> SearchAsync(QuestionnaireStatus? status, string titleContains, string specialtyCode, int skip, int take)
        {
            var q = _ctx.Questionnaires.AsQueryable().Where(x => !x.IsDeleted);
            if (status.HasValue) q = q.Where(x => x.Status == status.Value);
            if (!string.IsNullOrEmpty(titleContains)) q = q.Where(x => x.Title.Contains(titleContains));
            if (!string.IsNullOrEmpty(specialtyCode)) q = q.Where(x => x.SpecialtyCode == specialtyCode);
            return await q.OrderByDescending(x => x.UpdatedAt).Skip(skip).Take(take).ToListAsync();
        }

        public Task<int> CountAsync(QuestionnaireStatus? status, string titleContains, string specialtyCode)
        {
            var q = _ctx.Questionnaires.AsQueryable().Where(x => !x.IsDeleted);
            if (status.HasValue) q = q.Where(x => x.Status == status.Value);
            if (!string.IsNullOrEmpty(titleContains)) q = q.Where(x => x.Title.Contains(titleContains));
            if (!string.IsNullOrEmpty(specialtyCode)) q = q.Where(x => x.SpecialtyCode == specialtyCode);
            return q.CountAsync();
        }

        public async Task AddAsync(Questionnaire q)
        {
            _ctx.Questionnaires.Add(q);
            await _ctx.SaveChangesAsync();
        }

        public Task UpdateAsync(Questionnaire q) => _ctx.SaveChangesAsync();

        public async Task SoftDeleteAsync(Guid id)
        {
            var q = await _ctx.Questionnaires.FindAsync(id);
            if (q == null) return;
            q.IsDeleted = true;
            await _ctx.SaveChangesAsync();
        }

        public async Task AddVersionAsync(QuestionnaireVersion v)
        {
            _ctx.QuestionnaireVersions.Add(v);
            await _ctx.SaveChangesAsync();
        }

        public Task<QuestionnaireVersion> GetVersionAsync(Guid questionnaireId, int versionNumber)
        {
            return _ctx.QuestionnaireVersions
                .FirstOrDefaultAsync(v => v.QuestionnaireId == questionnaireId && v.VersionNumber == versionNumber);
        }
    }
}
