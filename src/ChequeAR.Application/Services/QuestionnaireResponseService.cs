using System;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Application.Services
{
    public class QuestionnaireResponseService
    {
        private readonly IQuestionnaireResponseRepository _repo;
        private readonly IQuestionnaireRepository _qRepo;
        private readonly IFhirValidator _validator;
        private readonly AuditDispatcher _audit;

        public QuestionnaireResponseService(
            IQuestionnaireResponseRepository repo,
            IQuestionnaireRepository qRepo,
            IFhirValidator validator,
            AuditDispatcher audit)
        {
            _repo = repo;
            _qRepo = qRepo;
            _validator = validator;
            _audit = audit;
        }

        public async Task<QuestionnaireResponse> CreateAsync(QuestionnaireResponse r, string userId)
        {
            if (string.IsNullOrEmpty(r.PatientHospitalId))
                throw new ArgumentException("PatientHospitalId is required");

            var q = await _qRepo.GetAsync(r.QuestionnaireId);
            if (q == null) throw new InvalidOperationException("Questionnaire not found");
            if (q.Status == QuestionnaireStatus.Retired)
                throw new InvalidOperationException("Cannot answer retired questionnaire");

            r.Id = Guid.NewGuid();
            r.AuthoredAt = DateTime.UtcNow;
            r.LastSavedAt = DateTime.UtcNow;
            r.AuthorUserId = userId;
            if (r.Status == 0) r.Status = ResponseStatus.InProgress;

            await _repo.AddAsync(r);
            _audit.Enqueue(AuditEventBuilder.For("create", "QuestionnaireResponse/" + r.Id, userId));
            return r;
        }

        public async Task<QuestionnaireResponse> FinalizeAsync(Guid id, string userId)
        {
            var r = await _repo.GetAsync(id);
            if (r == null) throw new InvalidOperationException("Not found");
            if (r.Status == ResponseStatus.Completed)
                return r;

            var validation = _validator.Validate(r.FhirJson);
            if (!validation.IsValid)
                throw new InvalidOperationException("Validation failed: " + string.Join("; ", validation.Errors));

            r.Status = ResponseStatus.Completed;
            r.LastSavedAt = DateTime.UtcNow;
            await _repo.UpdateAsync(r);
            _audit.Enqueue(AuditEventBuilder.For("update", "QuestionnaireResponse/" + r.Id + "$finalize", userId));
            return r;
        }
    }
}
