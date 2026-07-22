using System;
using System.Threading.Tasks;
using ChequeAR.Application.Fhir;
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

            // Los items llegan del cliente sin Id (Guid.Empty) y EF no genera Guids: sin
            // esto, dos respuestas cualesquiera colisionan en la PK y la ficha entera falla
            // con ORA-00001. Sólo se podían guardar fichas de una sola pregunta.
            if (r.Items != null)
            {
                foreach (var item in r.Items)
                {
                    if (item.Id == Guid.Empty) item.Id = Guid.NewGuid();
                    item.ResponseId = r.Id;
                }
            }

            // El recurso FHIR es la representación canónica de la respuesta y lo que valida
            // $finalize. Se arma acá porque es el único momento donde tenemos el
            // cuestionario a mano para resolver los textos de cada linkId.
            r.FhirJson = FhirQuestionnaireResponseBuilder.Build(r, q);

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

            // Se regenera con status completed antes de validar: el recurso guardado al
            // crear dice "in-progress" y quedaría contradiciendo el estado final.
            r.Status = ResponseStatus.Completed;
            var questionnaire = await _qRepo.GetAsync(r.QuestionnaireId);
            r.FhirJson = FhirQuestionnaireResponseBuilder.Build(r, questionnaire);

            var validation = _validator.Validate(r.FhirJson);
            if (!validation.IsValid)
            {
                r.Status = ResponseStatus.InProgress;   // no se finaliza nada inválido
                throw new InvalidOperationException("Validation failed: " + string.Join("; ", validation.Errors));
            }

            r.LastSavedAt = DateTime.UtcNow;
            await _repo.UpdateAsync(r);
            _audit.Enqueue(AuditEventBuilder.For("update", "QuestionnaireResponse/" + r.Id + "$finalize", userId));
            return r;
        }
    }
}
