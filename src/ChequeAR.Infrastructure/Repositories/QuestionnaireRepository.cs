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
    public class QuestionnaireRepository : IQuestionnaireRepository
    {
        private readonly ChequeARDbContext _ctx;

        public QuestionnaireRepository(ChequeARDbContext ctx) { _ctx = ctx; }

        public Task<Questionnaire> GetAsync(Guid id)
        {
            return _ctx.Questionnaires
                .Include(q => q.Versions)
                .Include(q => q.Groups)
                .Include(q => q.Questions)
                .Include(q => q.Questions.Select(x => x.Options))
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

        public Task UpdateAsync(Questionnaire q) => SaveAsync();

        public async Task UpdateWithStructureAsync(Questionnaire q, ICollection<QuestionGroup> groups, ICollection<Question> questions)
        {
            using (var tx = _ctx.Database.BeginTransaction())
            {
                // La cabecera PRIMERO y en su propio SaveChanges: lleva el token de
                // concurrencia, así que si otra persona tocó el cuestionario esto explota
                // acá, antes de haber borrado una sola pregunta. Todo dentro de la misma
                // transacción, de modo que un fallo posterior tampoco deja nada a medias.
                await SaveAsync();
                await ReplaceStructureAsync(q, groups, questions);
                tx.Commit();
            }
        }

        public async Task PublishAsync(Questionnaire q, QuestionnaireVersion version)
        {
            // Versión congelada y cabecera en una sola transacción: si se cortara entre las
            // dos, quedaba una versión huérfana y el cuestionario sin publicar.
            using (var tx = _ctx.Database.BeginTransaction())
            {
                _ctx.QuestionnaireVersions.Add(version);
                await SaveAsync();
                tx.Commit();
            }
        }

        // Traduce el choque de concurrencia de EF al lenguaje de la aplicación. Salta
        // cuando el UPDATE afecta 0 filas porque el ConcurrencyVersion del WHERE ya no
        // coincide: alguien más guardó en el medio.
        private async Task SaveAsync()
        {
            try
            {
                await _ctx.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                var stale = ex.Entries.Select(e => e.Entity).OfType<Questionnaire>().FirstOrDefault();
                var expected = stale?.ConcurrencyVersion ?? 0;
                var actual = expected;
                if (stale != null)
                {
                    var fromDb = await _ctx.Questionnaires.AsNoTracking()
                        .FirstOrDefaultAsync(x => x.Id == stale.Id);
                    actual = fromDb?.ConcurrencyVersion ?? expected;
                }
                throw new ConcurrencyConflictException(expected, actual);
            }
        }

        private async Task ReplaceStructureAsync(Questionnaire q, ICollection<QuestionGroup> groups, ICollection<Question> questions)
        {
            // Borrado explícito: las FKs están con WillCascadeOnDelete(false), así que hay
            // que bajar de opciones -> preguntas -> secciones a mano y en ese orden.
            var oldQuestions = await _ctx.Questions.Where(x => x.QuestionnaireId == q.Id).ToListAsync();
            var oldQuestionIds = oldQuestions.Select(x => x.Id).ToList();
            var oldOptions = await _ctx.AnswerOptions.Where(o => oldQuestionIds.Contains(o.QuestionId)).ToListAsync();
            var oldGroups = await _ctx.QuestionGroups.Where(g => g.QuestionnaireId == q.Id).ToListAsync();

            _ctx.AnswerOptions.RemoveRange(oldOptions);
            _ctx.Questions.RemoveRange(oldQuestions);
            _ctx.QuestionGroups.RemoveRange(oldGroups);

            // Las colecciones de la entidad trackeada apuntan a las filas recién borradas:
            // vaciarlas evita que EF intente re-insertarlas al guardar.
            q.Groups.Clear();
            q.Questions.Clear();

            foreach (var g in groups ?? new List<QuestionGroup>())
            {
                var newQuestions = g.Questions?.ToList() ?? new List<Question>();
                g.Questions.Clear();   // se agregan sueltas: la navegación se resuelve por GroupId
                _ctx.QuestionGroups.Add(g);
                q.Groups.Add(g);
                foreach (var question in newQuestions) AddQuestion(q, question);
            }
            foreach (var question in questions ?? new List<Question>()) AddQuestion(q, question);

            await SaveAsync();
        }

        private void AddQuestion(Questionnaire q, Question question)
        {
            var options = question.Options?.ToList() ?? new List<AnswerOption>();
            question.Options.Clear();
            _ctx.Questions.Add(question);
            q.Questions.Add(question);
            foreach (var opt in options)
            {
                opt.QuestionId = question.Id;
                _ctx.AnswerOptions.Add(opt);
                question.Options.Add(opt);
            }
        }

        public async Task SoftDeleteAsync(Guid id)
        {
            var q = await _ctx.Questionnaires.FindAsync(id);
            if (q == null) return;
            q.IsDeleted = true;
            await _ctx.SaveChangesAsync();
        }

        public Task<QuestionnaireVersion> GetVersionAsync(Guid questionnaireId, int versionNumber)
        {
            return _ctx.QuestionnaireVersions
                .FirstOrDefaultAsync(v => v.QuestionnaireId == questionnaireId && v.VersionNumber == versionNumber);
        }

        public async Task<IReadOnlyDictionary<Guid, string>> GetTitlesAsync(IEnumerable<Guid> ids)
        {
            var list = (ids ?? Enumerable.Empty<Guid>()).Distinct().ToList();
            if (list.Count == 0) return new Dictionary<Guid, string>();
            var rows = await _ctx.Questionnaires
                .Where(q => list.Contains(q.Id))
                .Select(q => new { q.Id, q.Title })
                .ToListAsync();
            return rows.ToDictionary(r => r.Id, r => r.Title);
        }
    }
}
