using System;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Exceptions;
using ChequeAR.Application.Fhir;
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
            q.HasPendingChanges = false;   // todavía no hay versión publicada que contradecir
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

        /// <param name="expectedVersion">
        /// ConcurrencyVersion que el cliente tenía al abrir el editor (llega por If-Match).
        /// Si no coincide con la de la base, alguien más guardó en el medio y se rechaza en
        /// vez de pisar su trabajo.
        /// </param>
        public async Task<Questionnaire> UpdateAsync(Guid id, Questionnaire patch, string userId, int? expectedVersion = null)
        {
            var q = await _repo.GetAsync(id);
            if (q == null) throw new InvalidOperationException("Not found");
            if (q.Status == QuestionnaireStatus.Retired)
                throw new InvalidOperationException("Retired questionnaires cannot be edited");

            // Chequeo temprano: da un error claro sin tocar la base. La carrera que queda
            // entre este read y el write la ataja el token de concurrencia en el UPDATE.
            if (expectedVersion.HasValue && q.ConcurrencyVersion != expectedVersion.Value)
                throw new ConcurrencyConflictException(expectedVersion.Value, q.ConcurrencyVersion);

            q.Title = patch.Title ?? q.Title;
            q.Description = patch.Description ?? q.Description;
            q.SpecialtyCode = patch.SpecialtyCode ?? q.SpecialtyCode;
            q.UpdatedAt = DateTime.UtcNow;
            // Toda edición deja cambios pendientes de publicar. En un borrador el flag es
            // inocuo (HasUnpublishedChanges() exige estado Activo).
            q.HasPendingChanges = true;
            q.ConcurrencyVersion++;

            // Estructura (secciones/preguntas/opciones): si el patch trae alguna, reemplaza
            // por completo la existente. Un patch sin estructura (colecciones vacías, que es
            // lo que deja el binder cuando el cliente no manda "questions") no la toca.
            var hasStructure = (patch.Groups != null && patch.Groups.Count > 0)
                            || (patch.Questions != null && patch.Questions.Count > 0);
            if (hasStructure)
            {
                var replacement = new Questionnaire { Id = q.Id, Groups = patch.Groups, Questions = patch.Questions };
                AssignChildIds(replacement);
                await _repo.UpdateWithStructureAsync(q, replacement.Groups, replacement.Questions);
            }
            else
            {
                await _repo.UpdateAsync(q);
            }

            _audit.Enqueue(AuditEventBuilder.For("update", "Questionnaire/" + q.Id, userId));
            return q;
        }

        public async Task<Questionnaire> PublishAsync(Guid id, string fhirJson, string userId)
        {
            var q = await _repo.GetAsync(id);
            if (q == null) throw new InvalidOperationException("Not found");
            if (!q.CanPublish()) throw new InvalidOperationException("Questionnaire cannot be published in current state");

            var nextVersionNumber = (q.Versions?.Count ?? 0) + 1;

            // Publicar desde el listado (Q1/Q6) no manda JSON: lo armamos desde la base.
            // El editor, en cambio, puede mandar el suyo y ese tiene prioridad.
            if (string.IsNullOrWhiteSpace(fhirJson))
                fhirJson = FhirQuestionnaireBuilder.Build(q, nextVersionNumber);

            var validation = _validator.Validate(fhirJson, FhirQuestionnaireBuilder.ProfileUrl);
            if (!validation.IsValid)
                throw new InvalidOperationException("FHIR validation failed: " + string.Join("; ", validation.Errors));

            // Un único timestamp para la versión y para el cuestionario: no lo necesita
            // HasPendingChanges (que es explícito), pero deja coherente el "última
            // modificación" del listado con la fecha de publicación.
            var now = DateTime.UtcNow;
            var version = new QuestionnaireVersion
            {
                Id = Guid.NewGuid(),
                QuestionnaireId = q.Id,
                VersionNumber = nextVersionNumber,
                FhirJson = fhirJson,
                PublishedAt = now,
                PublishedBy = userId
            };
            q.CurrentVersionId = version.Id;
            q.Status = QuestionnaireStatus.Active;
            q.HasPendingChanges = false;   // lo pendiente acaba de quedar congelado en la versión
            q.UpdatedAt = now;
            q.ConcurrencyVersion++;
            await _repo.PublishAsync(q, version);

            _audit.Enqueue(AuditEventBuilder.For("update", "Questionnaire/" + q.Id + "$publish", userId));
            return q;
        }

        public async Task ArchiveAsync(Guid id, string userId)
        {
            var q = await _repo.GetAsync(id);
            if (q == null) throw new InvalidOperationException("Not found");
            q.Status = QuestionnaireStatus.Retired;
            q.UpdatedAt = DateTime.UtcNow;
            q.ConcurrencyVersion++;
            await _repo.UpdateAsync(q);
            _audit.Enqueue(AuditEventBuilder.For("update", "Questionnaire/" + q.Id + "$archive", userId));
        }
    }
}
