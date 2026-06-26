using System;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Application.Services
{
    public class QuestionnaireService
    {
        private readonly IQuestionnaireRepository _repo;
        private readonly IFhirValidator _validator;
        private readonly AuditDispatcher _audit;

        public QuestionnaireService(IQuestionnaireRepository repo, IFhirValidator validator, AuditDispatcher audit)
        {
            _repo = repo;
            _validator = validator;
            _audit = audit;
        }

        public async Task<Questionnaire> CreateAsync(Questionnaire q, string userId)
        {
            q.Id = Guid.NewGuid();
            q.Status = QuestionnaireStatus.Draft;
            q.CreatedBy = userId;
            q.CreatedAt = DateTime.UtcNow;
            q.UpdatedAt = DateTime.UtcNow;

            // Asignar PKs/FKs a las entidades hijas: vienen del cliente sin Id (Guid.Empty),
            // y EF no genera Guids automáticamente -> sin esto, 2+ hijas colisionan en la PK.
            AssignChildIds(q);

            await _repo.AddAsync(q);
            _audit.Enqueue(AuditEventBuilder.For("create", "Questionnaire/" + q.Id, userId));
            return q;
        }

        private static void AssignChildIds(Questionnaire q)
        {
            if (q.Groups != null)
            {
                var gOrder = 0;
                foreach (var g in q.Groups)
                {
                    if (g.Id == Guid.Empty) g.Id = Guid.NewGuid();
                    g.QuestionnaireId = q.Id;
                    if (g.OrderIndex == 0) g.OrderIndex = gOrder;
                    gOrder++;
                    AssignQuestionIds(g.Questions, q.Id, g.Id);
                }
            }
            AssignQuestionIds(q.Questions, q.Id, null);
        }

        private static void AssignQuestionIds(System.Collections.Generic.ICollection<Question> questions, Guid questionnaireId, Guid? groupId)
        {
            if (questions == null) return;
            var order = 0;
            foreach (var question in questions)
            {
                if (question.Id == Guid.Empty) question.Id = Guid.NewGuid();
                question.QuestionnaireId = questionnaireId;
                if (groupId.HasValue) question.GroupId = groupId;
                if (question.OrderIndex == 0) question.OrderIndex = order;
                order++;

                if (question.Options != null)
                {
                    var oOrder = 0;
                    foreach (var opt in question.Options)
                    {
                        if (opt.Id == Guid.Empty) opt.Id = Guid.NewGuid();
                        opt.QuestionId = question.Id;
                        if (opt.OrderIndex == 0) opt.OrderIndex = oOrder;
                        oOrder++;
                    }
                }
            }
        }

        public async Task<Questionnaire> UpdateAsync(Guid id, Questionnaire patch, string userId)
        {
            var q = await _repo.GetAsync(id);
            if (q == null) throw new InvalidOperationException("Not found");
            if (q.Status == QuestionnaireStatus.Retired)
                throw new InvalidOperationException("Retired questionnaires cannot be edited");

            q.Title = patch.Title ?? q.Title;
            q.Description = patch.Description ?? q.Description;
            q.SpecialtyCode = patch.SpecialtyCode ?? q.SpecialtyCode;
            q.UpdatedAt = DateTime.UtcNow;

            await _repo.UpdateAsync(q);
            _audit.Enqueue(AuditEventBuilder.For("update", "Questionnaire/" + q.Id, userId));
            return q;
        }

        public async Task<Questionnaire> PublishAsync(Guid id, string fhirJson, string userId)
        {
            var q = await _repo.GetAsync(id);
            if (q == null) throw new InvalidOperationException("Not found");
            if (!q.CanPublish()) throw new InvalidOperationException("Questionnaire cannot be published in current state");

            var validation = _validator.Validate(fhirJson, "https://chequear.garrahan/StructureDefinition/ChequeARQuestionnaire");
            if (!validation.IsValid)
                throw new InvalidOperationException("FHIR validation failed: " + string.Join("; ", validation.Errors));

            var nextVersionNumber = (q.Versions?.Count ?? 0) + 1;
            var version = new QuestionnaireVersion
            {
                Id = Guid.NewGuid(),
                QuestionnaireId = q.Id,
                VersionNumber = nextVersionNumber,
                FhirJson = fhirJson,
                PublishedAt = DateTime.UtcNow,
                PublishedBy = userId
            };
            await _repo.AddVersionAsync(version);

            q.CurrentVersionId = version.Id;
            q.Status = QuestionnaireStatus.Active;
            q.UpdatedAt = DateTime.UtcNow;
            await _repo.UpdateAsync(q);

            _audit.Enqueue(AuditEventBuilder.For("update", "Questionnaire/" + q.Id + "$publish", userId));
            return q;
        }

        public async Task ArchiveAsync(Guid id, string userId)
        {
            var q = await _repo.GetAsync(id);
            if (q == null) throw new InvalidOperationException("Not found");
            q.Status = QuestionnaireStatus.Retired;
            q.UpdatedAt = DateTime.UtcNow;
            await _repo.UpdateAsync(q);
            _audit.Enqueue(AuditEventBuilder.For("update", "Questionnaire/" + q.Id + "$archive", userId));
        }
    }
}
