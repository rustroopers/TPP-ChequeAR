using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Exceptions;
using ChequeAR.Application.Fhir;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Security;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Application.Services
{
    /// <summary>Qué terminó pasando al pedir la baja de un cuestionario.</summary>
    public enum DeleteOutcome
    {
        Deleted,
        /// <summary>Tenía respuestas cargadas: se archivó en vez de eliminarse.</summary>
        ArchivedBecauseInUse
    }

    public class QuestionnaireService
    {
        // Tope del scan en memoria cuando el usuario está restringido a un subconjunto de
        // especialidades: los repos no aceptan una lista de códigos permitidos (harían
        // falta cambiar todas las firmas de búsqueda), así que se trae una tanda acotada y
        // se filtra/pagina acá. Questionnaire son decenas de filas en este dominio -- el
        // costo de sobre-traer es bajo. Documentado como simplificación deliberada.
        private const int MaxScopedScan = 2000;

        private static readonly string[] SpecialtyRequiredError =
            { "El cuestionario debe tener una especialidad." };

        private readonly IQuestionnaireRepository _repo;
        private readonly IQuestionnaireResponseRepository _responses;
        private readonly IFhirValidator _validator;
        private readonly AuditDispatcher _audit;
        private readonly ISpecialtyGuard _guard;
        private readonly SpecialtyCatalogService _catalog;

        public QuestionnaireService(
            IQuestionnaireRepository repo,
            IQuestionnaireResponseRepository responses,
            IFhirValidator validator,
            AuditDispatcher audit,
            ISpecialtyGuard guard,
            SpecialtyCatalogService catalog)
        {
            _repo = repo;
            _responses = responses;
            _validator = validator;
            _audit = audit;
            _guard = guard;
            _catalog = catalog;
        }

        // ── Lecturas ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Null si no existe. Lanza SpecialtyForbiddenException si existe pero está fuera del
        /// scope del usuario -- el controller lo traduce a 404 (no confirmar existencia de
        /// un recurso ajeno).
        /// </summary>
        public async Task<Questionnaire> GetAsync(Guid id)
        {
            var q = await _repo.GetAsync(id);
            if (q == null) return null;
            _guard.EnsureAllows(q.SpecialtyCode);
            return q;
        }

        public async Task<IReadOnlyList<Questionnaire>> SearchAsync(
            QuestionnaireStatus? status, string titleContains, string specialtyCode, int skip, int take)
        {
            var scope = _guard.Scope;
            // Un filtro explícito por especialidad que el usuario no tiene: se rechaza (403),
            // no se devuelve vacío -- devolver vacío le mentiría ("no hay nada") en vez de
            // decirle que no puede pedir eso.
            if (!string.IsNullOrEmpty(specialtyCode)) _guard.EnsureAllows(specialtyCode);

            if (scope.IsUnrestricted)
                return await _repo.SearchAsync(status, titleContains, specialtyCode, skip, take);
            if (scope.IsEmpty) return new List<Questionnaire>();

            var scanned = await _repo.SearchAsync(status, titleContains, specialtyCode, 0, MaxScopedScan);
            return scanned.Where(q => scope.Allows(q.SpecialtyCode)).Skip(skip).Take(take).ToList();
        }

        public async Task<int> CountAsync(QuestionnaireStatus? status, string titleContains, string specialtyCode)
        {
            var scope = _guard.Scope;
            if (!string.IsNullOrEmpty(specialtyCode)) _guard.EnsureAllows(specialtyCode);
            if (scope.IsUnrestricted) return await _repo.CountAsync(status, titleContains, specialtyCode);
            if (scope.IsEmpty) return 0;

            var scanned = await _repo.SearchAsync(status, titleContains, specialtyCode, 0, MaxScopedScan);
            return scanned.Count(q => scope.Allows(q.SpecialtyCode));
        }

        /// <summary>
        /// Cuestionarios que sirven como fuente para "importar preguntas" al crear/editar otro:
        /// misma especialidad, no archivados, y distintos del que se está editando. Un archivado
        /// no se ofrece —su estructura sigue siendo legible, pero como base de trabajo nuevo no
        /// aporta y confunde—. El propio cuestionario se excluye para no importarse a sí mismo.
        /// </summary>
        public async Task<IReadOnlyList<Questionnaire>> GetImportCandidatesAsync(string specialtyCode, Guid? excludeId)
        {
            _guard.EnsureAllows(specialtyCode);
            var candidatos = await _repo.SearchAsync(null, null, specialtyCode, 0, 200);
            return candidatos
                .Where(q => q.Status != QuestionnaireStatus.Retired
                            && (!excludeId.HasValue || q.Id != excludeId.Value))
                .ToList();
        }

        /// <summary>Códigos de especialidad con actividad, recortados al scope del usuario (las opciones del filtro no pueden nombrar especialidades ajenas).</summary>
        public async Task<IReadOnlyList<string>> GetSpecialtyCodesAsync()
        {
            var scope = _guard.Scope;
            var codes = await _repo.GetSpecialtyCodesAsync();
            return scope.IsUnrestricted ? codes : codes.Where(scope.Allows).ToList();
        }

        public Task<IReadOnlyDictionary<Guid, string>> GetTitlesAsync(IEnumerable<Guid> ids)
            => _repo.GetTitlesAsync(ids);

        public Task<QuestionnaireVersion> GetVersionAsync(Guid questionnaireId, int versionNumber)
            => _repo.GetVersionAsync(questionnaireId, versionNumber);

        /// <summary>
        /// Especialidad obligatoria, tiene que existir y estar activa en el catálogo, y el
        /// usuario tiene que tener acceso a ella. Único punto de validación: lo usan
        /// CreateAsync, UpdateAsync (cuando cambia la especialidad) y, indirectamente,
        /// CloneAsync (que hereda del origen sin volver a preguntarle al catálogo).
        /// </summary>
        private async Task EnsureSpecialtyUsableAsync(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ValidationFailedException(SpecialtyRequiredError);
            if (!await _catalog.ExistsActiveAsync(code))
                throw new ValidationFailedException(new[] { "Especialidad desconocida o inactiva: " + code });
            _guard.EnsureAllows(code);
        }

        public async Task<Questionnaire> CreateAsync(Questionnaire q, string userId)
        {
            await EnsureSpecialtyUsableAsync(q.SpecialtyCode);
            // El editor guarda sólo el código (SpecialtyDisplay llega null): se completa acá
            // desde el catálogo para que el useContext del recurso FHIR (armado por
            // FhirQuestionnaireBuilder a partir de estos dos campos) tenga un display legible
            // sin que el builder tenga que conocer el catálogo.
            var cat = await _catalog.GetAsync();
            q.SpecialtyDisplay = cat.Label(q.SpecialtyCode, q.SpecialtyDisplay);

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
            // ¿Puedo tocar ESTE cuestionario? (chequeo sobre el código ACTUAL, antes que nada).
            _guard.EnsureAllows(q.SpecialtyCode);
            if (q.Status == QuestionnaireStatus.Retired)
                throw new InvalidOperationException("Retired questionnaires cannot be edited");

            // Chequeo temprano: da un error claro sin tocar la base. La carrera que queda
            // entre este read y el write la ataja el token de concurrencia en el UPDATE.
            if (expectedVersion.HasValue && q.ConcurrencyVersion != expectedVersion.Value)
                throw new ConcurrencyConflictException(expectedVersion.Value, q.ConcurrencyVersion);

            // ¿Puedo moverlo ALLÁ? Sólo si de verdad cambia (evita rechazar un PUT que repite
            // el mismo código). Chequear los DOS sentidos evita tanto "robarse" un cuestionario
            // ajeno declarándolo de la propia especialidad, como "expulsarlo" moviéndolo a una
            // especialidad ajena.
            if (patch.SpecialtyCode != null && !string.Equals(patch.SpecialtyCode, q.SpecialtyCode, StringComparison.Ordinal))
                await EnsureSpecialtyUsableAsync(patch.SpecialtyCode);

            q.Title = patch.Title ?? q.Title;
            q.Description = patch.Description ?? q.Description;
            var cat = await _catalog.GetAsync();
            if (patch.SpecialtyCode != null)
            {
                q.SpecialtyCode = patch.SpecialtyCode;
                q.SpecialtyDisplay = cat.Label(patch.SpecialtyCode, patch.SpecialtyDisplay);
            }
            // A diferencia de los campos de arriba, la codificación se asigna directo (sin el
            // "?? valor viejo"): es metadata clínica OPCIONAL que tiene que poder QUITARSE, y un
            // PUT FHIR es un reemplazo completo del recurso —un cuestionario sin code ni término
            // significa "sin codificar", no "no lo toques"—. Así se logra la misma paridad que
            // en las preguntas, donde quitar el término también es posible.
            q.CodingJson = patch.CodingJson;
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

        /// <summary>
        /// Congela una versión. El recurso se arma SIEMPRE desde la base.
        ///
        /// Antes aceptába también un JSON del cliente, con prioridad sobre lo construido:
        /// tenía sentido cuando el editor mantenía su propia representación. Con una sola
        /// superficie FHIR ningún cliente lo manda, y dejarlo abierto permitía congelar una
        /// versión cuyo contenido no se corresponde con la entidad — es decir, mentirle a
        /// quien después responda esa ficha.
        /// </summary>
        public async Task<Questionnaire> PublishAsync(Guid id, string userId, string changeNote = null)
        {
            var q = await _repo.GetAsync(id);
            if (q == null) throw new InvalidOperationException("Not found");
            _guard.EnsureAllows(q.SpecialtyCode);
            if (!q.CanPublish()) throw new InvalidOperationException("Questionnaire cannot be published in current state");

            var nextVersionNumber = (q.Versions?.Count ?? 0) + 1;

            var fhirJson = FhirQuestionnaireBuilder.Build(q, nextVersionNumber);

            var validation = _validator.Validate(fhirJson, FhirQuestionnaireBuilder.ProfileUrl);
            if (!validation.IsValid)
                throw new ValidationFailedException(validation.Errors);

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
                PublishedBy = userId,
                ChangeNote = string.IsNullOrWhiteSpace(changeNote) ? null : changeNote.Trim()
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

        /// <summary>
        /// Copia estructura completa en un cuestionario nuevo, siempre en Borrador y sin
        /// versiones publicadas: es un punto de partida, no una continuación del original.
        /// </summary>
        public async Task<Questionnaire> CloneAsync(Guid id, string newTitle, string userId)
        {
            var source = await _repo.GetAsync(id);
            if (source == null) throw new InvalidOperationException("Not found");
            _guard.EnsureAllows(source.SpecialtyCode);

            var copy = new Questionnaire
            {
                Title = string.IsNullOrWhiteSpace(newTitle) ? source.Title + " (copia)" : newTitle,
                Description = source.Description,
                SpecialtyCode = source.SpecialtyCode,
                SpecialtyDisplay = source.SpecialtyDisplay,
                CodingJson = source.CodingJson
            };

            // Los linkId se conservan a propósito: son la referencia de las respuestas y
            // mantenerlos hace que un clon siga siendo comparable con el original en reportes.
            foreach (var g in (source.Groups ?? new List<QuestionGroup>()).OrderBy(x => x.OrderIndex))
            {
                var newGroup = new QuestionGroup { LinkId = g.LinkId, Title = g.Title, OrderIndex = g.OrderIndex };
                foreach (var question in (source.Questions ?? new List<Question>())
                             .Where(x => x.GroupId == g.Id).OrderBy(x => x.OrderIndex))
                    newGroup.Questions.Add(CopyQuestion(question));
                copy.Groups.Add(newGroup);
            }
            foreach (var question in (source.Questions ?? new List<Question>())
                         .Where(x => !x.GroupId.HasValue).OrderBy(x => x.OrderIndex))
                copy.Questions.Add(CopyQuestion(question));

            // CreateAsync exige especialidad activa: como se hereda de un origen que ya
            // existe, se valida el acceso pero NO el catálogo (una especialidad dada de baja
            // no debe impedir clonar algo que ya la tenía).
            copy.Id = Guid.NewGuid();
            copy.Status = QuestionnaireStatus.Draft;
            copy.HasPendingChanges = false;
            copy.CreatedBy = userId;
            copy.CreatedAt = DateTime.UtcNow;
            copy.UpdatedAt = DateTime.UtcNow;
            AssignChildIds(copy);
            await _repo.AddAsync(copy);
            _audit.Enqueue(AuditEventBuilder.For("create", "Questionnaire/" + copy.Id, userId));

            _audit.Enqueue(AuditEventBuilder.For("create", "Questionnaire/" + copy.Id + "$clone-from-" + id, userId));
            return copy;
        }

        private static Question CopyQuestion(Question source)
        {
            var copy = new Question
            {
                LinkId = source.LinkId,
                Text = source.Text,
                Type = source.Type,
                Required = source.Required,
                RepeatsAllowed = source.RepeatsAllowed,
                OrderIndex = source.OrderIndex,
                ValidationJson = source.ValidationJson,
                EnableWhenJson = source.EnableWhenJson,
                CodingJson = source.CodingJson
            };
            foreach (var opt in (source.Options ?? new List<AnswerOption>()).OrderBy(o => o.OrderIndex))
                copy.Options.Add(new AnswerOption
                {
                    Value = opt.Value,
                    Display = opt.Display,
                    Code = opt.Code,
                    OrderIndex = opt.OrderIndex
                });
            return copy;
        }

        /// <summary>
        /// Baja lógica, con la salvaguarda de la spec: si ya hay respuestas cargadas el
        /// cuestionario NO se elimina —esas respuestas quedarían apuntando a algo que
        /// desapareció del listado— sino que se archiva.
        /// </summary>
        public async Task<DeleteOutcome> DeleteAsync(Guid id, string userId)
        {
            var q = await _repo.GetAsync(id);
            if (q == null) throw new InvalidOperationException("Not found");
            _guard.EnsureAllows(q.SpecialtyCode);

            if (await _responses.HasResponsesAsync(id))
            {
                if (q.Status != QuestionnaireStatus.Retired)
                {
                    q.Status = QuestionnaireStatus.Retired;
                    q.UpdatedAt = DateTime.UtcNow;
                    q.ConcurrencyVersion++;
                    await _repo.UpdateAsync(q);
                }
                _audit.Enqueue(AuditEventBuilder.For("update", "Questionnaire/" + id + "$archive-instead-of-delete", userId));
                return DeleteOutcome.ArchivedBecauseInUse;
            }

            await _repo.SoftDeleteAsync(id);
            _audit.Enqueue(AuditEventBuilder.For("delete", "Questionnaire/" + id, userId));
            return DeleteOutcome.Deleted;
        }

        public async Task ArchiveAsync(Guid id, string userId)
        {
            var q = await _repo.GetAsync(id);
            if (q == null) throw new InvalidOperationException("Not found");
            _guard.EnsureAllows(q.SpecialtyCode);
            if (q.Status == QuestionnaireStatus.Retired)
                throw new InvalidOperationException("Questionnaire is already archived");
            q.Status = QuestionnaireStatus.Retired;
            q.UpdatedAt = DateTime.UtcNow;
            q.ConcurrencyVersion++;
            await _repo.UpdateAsync(q);
            _audit.Enqueue(AuditEventBuilder.For("update", "Questionnaire/" + q.Id + "$archive", userId));
        }
    }
}
