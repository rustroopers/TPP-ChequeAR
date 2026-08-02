using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Exceptions;
using ChequeAR.Application.Fhir;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

        // ── Lecturas ──────────────────────────────────────────────────────────────────

        public Task<QuestionnaireResponse> GetAsync(Guid id) => _repo.GetAsync(id);

        public Task<IReadOnlyList<QuestionnaireResponse>> SearchAsync(
            string patientHospitalId, Guid? questionnaireId, ResponseStatus? status, int skip, int take)
            => _repo.SearchAsync(patientHospitalId, questionnaireId, status, skip, take);

        public Task<bool> HasResponsesAsync(Guid questionnaireId) => _repo.HasResponsesAsync(questionnaireId);

        public async Task<QuestionnaireResponse> CreateAsync(QuestionnaireResponse r, string userId)
        {
            if (string.IsNullOrEmpty(r.PatientHospitalId))
                throw new ArgumentException("PatientHospitalId is required");

            var q = await _qRepo.GetAsync(r.QuestionnaireId);
            if (q == null) throw new InvalidOperationException("Questionnaire not found");
            if (q.Status == QuestionnaireStatus.Retired)
                throw new InvalidOperationException("Cannot answer retired questionnaire");

            // Anclaje de versión: la ficha queda atada a la versión con la que se completó,
            // aunque se publique otra en el medio. El cliente manda la que rendirizó; si no
            // la manda, se cae a la vigente. Sin versión publicada no hay nada que responder.
            var pinned = ResolveVersion(q, r.QuestionnaireVersionId);
            if (pinned == null)
                throw new InvalidOperationException("Questionnaire has no published version to answer");
            r.QuestionnaireVersionId = pinned.Id;

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
            // Los textos salen de la versión anclada, no de la entidad viva: si alguien
            // renombró una pregunta después, la ficha debe conservar lo que el médico leyó.
            // Se preservan los datos del paciente que venían embebidos: cuando el hospital
            // no responde, ese Patient "contained" es la única forma de saber de quién es
            // la ficha (lo lee PatientService en modo degradado).
            r.FhirJson = FhirQuestionnaireResponseBuilder.Build(
                r, FhirQuestionnaireReader.Read(pinned.FhirJson), DemographicsFrom(r.FhirJson));

            await _repo.AddAsync(r);
            _audit.Enqueue(AuditEventBuilder.For("create", "QuestionnaireResponse/" + r.Id, userId));
            return r;
        }

        /// <summary>
        /// Rescata los datos del paciente del recurso que ya venía guardado, para no
        /// perderlos al regenerarlo. Null si no había ninguno.
        /// </summary>
        private static PatientDemographicsDto DemographicsFrom(string existingFhirJson)
        {
            // Sólo se acepta un Patient "contained" de verdad. NO vale FhirPatientReader,
            // que cae a subject.display como nombre: como ahí escribimos la HC cuando no
            // hay nombre, regenerar el recurso al finalizar terminaba inventando un
            // paciente llamado "PAC-000001".
            if (string.IsNullOrWhiteSpace(existingFhirJson)) return null;
            try
            {
                var contained = JObject.Parse(existingFhirJson)["contained"] as JArray;
                foreach (var c in contained ?? new JArray())
                {
                    if ((string)c["resourceType"] != "Patient") continue;
                    var name = (string)c.SelectToken("name[0].text");
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    DateTime? birth = null;
                    var raw = (string)c["birthDate"];
                    if (!string.IsNullOrWhiteSpace(raw) && DateTime.TryParse(raw, out var parsed)) birth = parsed;

                    return new PatientDemographicsDto
                    {
                        FullName = name,
                        Gender = (string)c["gender"],
                        BirthDate = birth
                    };
                }
            }
            catch (JsonException) { /* recurso ilegible: sin datos de paciente */ }
            return null;
        }

        /// <summary>
        /// La versión pedida si existe; si no, la vigente. Null si el cuestionario nunca
        /// se publicó. Una versión ya no vigente sigue siendo válida: es justamente el
        /// caso de la ficha que se empezó antes de la última publicación.
        /// </summary>
        private static QuestionnaireVersion ResolveVersion(Questionnaire q, Guid requestedVersionId)
        {
            if (q?.Versions == null || q.Versions.Count == 0) return null;
            if (requestedVersionId != Guid.Empty)
            {
                var requested = q.Versions.FirstOrDefault(v => v.Id == requestedVersionId);
                if (requested != null) return requested;
            }
            if (q.CurrentVersionId.HasValue)
            {
                var current = q.Versions.FirstOrDefault(v => v.Id == q.CurrentVersionId.Value);
                if (current != null) return current;
            }
            return q.Versions.OrderByDescending(v => v.VersionNumber).First();
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
            var pinned = ResolveVersion(questionnaire, r.QuestionnaireVersionId);
            r.FhirJson = FhirQuestionnaireResponseBuilder.Build(
                r, FhirQuestionnaireReader.Read(pinned?.FhirJson), DemographicsFrom(r.FhirJson));

            var validation = _validator.Validate(r.FhirJson);
            if (!validation.IsValid)
            {
                r.Status = ResponseStatus.InProgress;   // no se finaliza nada inválido
                throw new ValidationFailedException(validation.Errors);
            }

            r.LastSavedAt = DateTime.UtcNow;
            await _repo.UpdateAsync(r);
            _audit.Enqueue(AuditEventBuilder.For("update", "QuestionnaireResponse/" + r.Id + "$finalize", userId));
            return r;
        }
    }
}
