using System;
using ChequeAR.Application.Dtos;
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

        private static readonly string[] TitleRequiredError =
            { "El cuestionario debe tener un título." };

        private readonly IQuestionnaireRepository _repo;
        private readonly IQuestionnaireResponseRepository _responses;
        private readonly IFhirValidator _validator;
        private readonly AuditDispatcher _audit;
        private readonly ISpecialtyGuard _guard;
        private readonly SpecialtyCatalogService _catalog;
        private readonly IAccessGuard _access;
        private readonly IClinicalVariableRepository _variables;

        public QuestionnaireService(
            IQuestionnaireRepository repo,
            IQuestionnaireResponseRepository responses,
            IFhirValidator validator,
            AuditDispatcher audit,
            ISpecialtyGuard guard,
            SpecialtyCatalogService catalog,
            IAccessGuard access,
            IClinicalVariableRepository variables)
        {
            _repo = repo;
            _responses = responses;
            _validator = validator;
            _audit = audit;
            _guard = guard;
            _catalog = catalog;
            _access = access;
            _variables = variables;
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

        /// <summary>
        /// Para rotular el historial de un paciente. Null si el cuestionario está fuera del scope
        /// (en lugar de lanzar como <see cref="GetAsync"/>).
        /// </summary>
        public async Task<Questionnaire> GetForPatientHistoryAsync(Guid id)
        {
            var q = await _repo.GetAsync(id);
            return q != null && _guard.Scope.Allows(q.SpecialtyCode) ? q : null;
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

        /// <summary>
        /// Cantidad de versiones publicadas de cada cuestionario, en una sola consulta. Los ids
        /// salen de <see cref="SearchAsync"/>, que ya aplicó el scope: esto sólo los completa.
        /// </summary>
        public Task<IReadOnlyDictionary<Guid, int>> GetVersionCountsAsync(IEnumerable<Guid> ids)
            => _repo.GetVersionCountsAsync(ids);

        /// <summary>
        /// Los resultados de una búsqueda con su estructura completa cargada, para serializarlos
        /// como recursos FHIR sin una consulta por colección y por fila. Se vuelve a aplicar el
        /// scope por si los ids no salieron de <see cref="SearchAsync"/>.
        /// </summary>
        public async Task<IReadOnlyList<Questionnaire>> GetManyWithStructureAsync(IReadOnlyList<Guid> ids)
        {
            var scope = _guard.Scope;
            var rows = await _repo.GetManyWithStructureAsync(ids);
            return scope.IsUnrestricted ? rows : rows.Where(q => scope.Allows(q.SpecialtyCode)).ToList();
        }

        public Task<IReadOnlyDictionary<Guid, string>> GetTitlesAsync(IEnumerable<Guid> ids)
            => _repo.GetTitlesAsync(ids);

        /// <summary>Todos los cuestionarios, eliminados incluidos, para el filtro de auditoría.</summary>
        public Task<IReadOnlyList<QuestionnaireTitle>> ListTitlesAsync()
            => _repo.ListTitlesAsync();

        public async Task<QuestionnaireVersion> GetVersionAsync(Guid questionnaireId, int versionNumber)
        {
            _guard.EnsureAllows(await _repo.GetSpecialtyCodeAsync(questionnaireId));
            return await _repo.GetVersionAsync(questionnaireId, versionNumber);
        }

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
            _access.EnsureCan(Permissions.QuestionnaireWrite);
            // Antes sólo lo exigía el perfil al publicar: un borrador sin título quedaba como una
            // fila en blanco en el listado, imposible de reconocer.
            if (string.IsNullOrWhiteSpace(q.Title)) throw new ValidationFailedException(TitleRequiredError);
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

            // Antes de AssignChildIds: el snapshot genera las AnswerOption desde la variable,
            // y tienen que existir para que ahí abajo reciban Id y OrderIndex.
            await BindVariablesAsync(q, new HashSet<Guid>(), NoPrevious);
            EnsureStructure(QuestionnaireStructureRules.CheckForSave(q.Groups, AllQuestions(q)));

            // Asignar PKs/FKs a las entidades hijas: vienen del cliente sin Id (Guid.Empty),
            // y EF no genera Guids automáticamente -> sin esto, 2+ hijas colisionan en la PK.
            AssignChildIds(q);

            await _repo.AddAsync(q);
            _audit.Enqueue(AuditEventBuilder.For("create", "Questionnaire/" + q.Id, userId));
            return q;
        }

        private static readonly IReadOnlyDictionary<string, Question> NoPrevious =
            new Dictionary<string, Question>();

        /// <summary>
        /// Todas las preguntas del cuestionario, estén sueltas o dentro de una sección, cada
        /// una UNA vez. La entidad tiene dos formas según de dónde venga: la que arma el parser
        /// cuelga las preguntas de cada sección, y la que carga EF las tiene en la colección
        /// plana y además (por el fixup de relaciones) en la de su sección.
        /// </summary>
        private static List<Question> AllQuestions(Questionnaire q)
        {
            var vistas = new HashSet<Question>();
            var result = new List<Question>();
            foreach (var question in q.Questions ?? new List<Question>())
                if (vistas.Add(question)) result.Add(question);
            foreach (var g in q.Groups ?? new List<QuestionGroup>())
                foreach (var question in g.Questions ?? new List<Question>())
                    if (vistas.Add(question)) result.Add(question);
            return result;
        }

        private static void EnsureStructure(IReadOnlyList<string> errors)
        {
            if (errors.Count > 0) throw new ValidationFailedException(errors);
        }

        /// <summary>
        /// Regla central de la feature: toda pregunta proviene de una variable del catálogo.
        /// Acá se verifica y se copia la definición de la variable a la pregunta.
        ///
        /// Vive en el servicio y no en el esquema ni en el perfil FHIR a propósito. En la base,
        /// un VariableId NOT NULL convertiría un recurso externo mal formado en un ORA-01400
        /// (500) en vez del 422 que corresponde; en el perfil, sería una restricción sobre una
        /// tabla interna de ChequeAR, que no es lo que un StructureDefinition describe.
        ///
        /// <paramref name="yaEnUso"/> son las variables que el cuestionario persistido ya tenía:
        /// esas pasan aunque estén inactivas, porque desactivar una variable no debe romper el
        /// guardado de un borrador que la venía usando.
        ///
        /// <paramref name="previas"/> son las preguntas persistidas, por linkId. Cada pregunta
        /// trae en VariableRevision la versión de la variable que pide quien guarda (0 si no pide
        /// ninguna):
        ///  - una que ya existía, con la misma variable y sin pedir otra versión, conserva el
        ///    snapshot con el que se armó: guardar no es una forma encubierta de actualizar
        ///    variables, y re-aplicar la vigente dejaría sin efecto, en silencio, las condiciones
        ///    que apuntan a una opción renombrada;
        ///  - si pide una versión (el botón "Actualizar" del editor, o el import, que conserva la
        ///    del origen), se aplica esa;
        ///  - una nueva sin pedido toma la vigente.
        /// </summary>
        private async Task BindVariablesAsync(
            Questionnaire target, HashSet<Guid> yaEnUso, IReadOnlyDictionary<string, Question> previas)
        {
            var questions = AllQuestions(target);
            if (questions.Count == 0) return;

            var errors = new List<string>();
            var sinVariable = questions.Where(x => !x.VariableId.HasValue || x.VariableId.Value == Guid.Empty).ToList();
            foreach (var x in sinVariable)
                errors.Add("La pregunta '" + (x.LinkId ?? x.Text) + "' no proviene del catálogo de variables.");

            var ids = questions.Where(x => x.VariableId.HasValue && x.VariableId.Value != Guid.Empty)
                               .Select(x => x.VariableId.Value).Distinct().ToList();
            // Una sola consulta para todo el cuestionario, no una por pregunta.
            var found = (await _variables.GetManyAsync(ids)).ToDictionary(v => v.Id);

            foreach (var id in ids.Where(i => !found.ContainsKey(i)))
                errors.Add("La variable " + id + " no existe en el catálogo.");

            foreach (var v in found.Values.Where(v => !v.IsActive && !yaEnUso.Contains(v.Id)))
                errors.Add("La variable '" + v.Name + "' está inactiva y no puede agregarse a un cuestionario.");

            if (errors.Count > 0) throw new ValidationFailedException(errors);

            var versions = (await _variables.GetVersionsAsync(ids))
                .ToDictionary(x => Tuple.Create(x.VariableId, x.VersionNumber));
            var vigente = found.Values.ToDictionary(v => v.Id, v =>
                versions.Values.FirstOrDefault(x => x.Id == v.CurrentVersionId)?.VersionNumber ?? 0);

            foreach (var question in questions)
            {
                var pedida = question.VariableRevision;
                if (question.LinkId != null
                    && previas.TryGetValue(question.LinkId, out var previa)
                    && previa.VariableId == question.VariableId
                    && (pedida <= 0 || pedida == previa.VariableRevision))
                {
                    KeepSnapshot(question, previa);
                    continue;
                }

                var variable = found[question.VariableId.Value];
                var numero = pedida > 0 ? pedida : vigente[variable.Id];
                if (numero == 0)
                    errors.Add("La variable '" + variable.Name + "' no tiene ninguna versión publicada.");
                else if (!versions.TryGetValue(Tuple.Create(variable.Id, numero), out var version))
                    errors.Add("La variable '" + variable.Name + "' no tiene una versión " + numero + ".");
                else
                    ClinicalVariableSnapshot.Apply(question, VariableDefinition.Parse(version.DefinitionJson), version);
            }
            if (errors.Count > 0) throw new ValidationFailedException(errors);
        }

        /// <summary>Lo que define la variable, tal como quedó congelado en la pregunta persistida.</summary>
        private static void KeepSnapshot(Question target, Question previa)
        {
            target.VariableVersionId = previa.VariableVersionId;
            target.VariableRevision = previa.VariableRevision;
            target.Type = previa.Type;
            target.RepeatsAllowed = previa.RepeatsAllowed;
            target.ValidationJson = previa.ValidationJson;
            target.CodingJson = previa.CodingJson;
            if (string.IsNullOrWhiteSpace(target.Text)) target.Text = previa.Text;
            target.Options = (previa.Options ?? new List<AnswerOption>())
                .OrderBy(o => o.OrderIndex)
                .Select(o => new AnswerOption
                {
                    Value = o.Value,
                    Display = o.Display,
                    Code = o.Code,
                    SnomedCode = o.SnomedCode,
                    SnomedDisplay = o.SnomedDisplay,
                    OrderIndex = o.OrderIndex
                })
                .ToList();
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
            _access.EnsureCan(Permissions.QuestionnaireWrite);
            var q = await _repo.GetAsync(id);
            if (q == null) throw new NotFoundException("El cuestionario no existe.");
            // ¿Puedo tocar ESTE cuestionario? (chequeo sobre el código ACTUAL, antes que nada).
            _guard.EnsureAllows(q.SpecialtyCode);
            if (q.Status == QuestionnaireStatus.Retired)
                throw new BusinessRuleException("Un cuestionario archivado no se puede editar.");

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

            // Un título que llega pero vacío es un intento de borrarlo, no un "no lo toques":
            // en Oracle '' es NULL, así que pasaba derecho y dejaba el cuestionario sin nombre.
            if (patch.Title != null && string.IsNullOrWhiteSpace(patch.Title))
                throw new ValidationFailedException(TitleRequiredError);

            q.Title = patch.Title ?? q.Title;
            q.Description = patch.Description;
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

            // Estructura (secciones/preguntas/opciones): el PUT es un reemplazo completo, así
            // que la que llega reemplaza a la existente, aunque venga vacía.
            var replacement = new Questionnaire
            {
                Id = q.Id,
                Groups = patch.Groups ?? new List<QuestionGroup>(),
                Questions = patch.Questions ?? new List<Question>()
            };
            // Las variables que el cuestionario YA usaba se aceptan aunque estén dadas de
            // baja: que el Administrador desactive una variable no debe romper el guardado
            // de un borrador ajeno que la tenía desde antes. Lo que se bloquea es el uso nuevo.
            var persistidas = AllQuestions(q);
            var yaEnUso = new HashSet<Guid>(persistidas.Where(x => x.VariableId.HasValue).Select(x => x.VariableId.Value));
            var previas = persistidas.Where(x => x.LinkId != null)
                                     .GroupBy(x => x.LinkId)
                                     .ToDictionary(g => g.Key, g => g.First());
            await BindVariablesAsync(replacement, yaEnUso, previas);
            EnsureStructure(QuestionnaireStructureRules.CheckForSave(replacement.Groups, AllQuestions(replacement)));
            EnsureStructure(PublishedLinkIds.Conflicts(PublishedLinkIds.From(q), AllQuestions(replacement)));
            AssignChildIds(replacement);
            await _repo.UpdateWithStructureAsync(q, replacement.Groups, replacement.Questions);

            _audit.Enqueue(AuditEventBuilder.For("update", "Questionnaire/" + q.Id, userId));
            return q;
        }

        /// <summary>
        /// Congela una versión. El recurso se arma SIEMPRE desde la base: nunca desde un JSON
        /// del cliente, que podría no corresponderse con la entidad.
        /// </summary>
        /// <param name="expectedVersion">
        /// ConcurrencyVersion que vio quien publica (If-Match). Si no coincide, alguien guardó
        /// cambios en el medio y se rechaza: publicar congela lo que hay en la base, y no se
        /// puede congelar algo que quien publica nunca vio.
        /// </param>
        public async Task<Questionnaire> PublishAsync(Guid id, string userId, string changeNote = null, int? expectedVersion = null)
        {
            _access.EnsureCan(Permissions.QuestionnaireWrite);
            var q = await _repo.GetAsync(id);
            if (q == null) throw new NotFoundException("El cuestionario no existe.");
            _guard.EnsureAllows(q.SpecialtyCode);
            if (expectedVersion.HasValue && q.ConcurrencyVersion != expectedVersion.Value)
                throw new ConcurrencyConflictException(expectedVersion.Value, q.ConcurrencyVersion);
            if (!q.CanPublish()) throw new BusinessRuleException("No hay nada para publicar: el cuestionario no tiene preguntas o no tiene cambios desde la última versión.");

            // Un archivado sin cambios desde su última versión se reactiva tal cual: congelar
            // otra versión idéntica sólo agregaría ruido al historial y al diff.
            if (q.Status == QuestionnaireStatus.Retired && !q.HasPendingChanges && q.CurrentVersion() != null)
            {
                q.Status = QuestionnaireStatus.Active;
                q.UpdatedAt = DateTime.UtcNow;
                q.ConcurrencyVersion++;
                await _repo.UpdateAsync(q);
                _audit.Enqueue(AuditEventBuilder.For("update", "Questionnaire/" + q.Id + "$unarchive", userId));
                return q;
            }

            var preguntas = AllQuestions(q);

            // Chequeo defensivo antes de congelar: una versión publicada tiene que ser
            // reproducible, y una pregunta sin variable no se puede explicar contra el catálogo.
            var huerfanas = preguntas.Where(x => !x.VariableId.HasValue).Select(x => x.LinkId).ToList();
            if (huerfanas.Count > 0)
                throw new ValidationFailedException(huerfanas
                    .Select(l => "La pregunta '" + l + "' no proviene del catálogo de variables y no se puede publicar."));

            // Armadas antes del versionado con una definición que ya no está publicada.
            var sinVersion = preguntas.Where(x => !x.VariableVersionId.HasValue).ToList();
            if (sinVersion.Count > 0)
                throw new ValidationFailedException(sinVersion.Select(x =>
                    "La pregunta \"" + (x.Text ?? x.LinkId) + "\" usa una definición anterior de su variable: actualizala desde el editor antes de publicar."));

            EnsureStructure(QuestionnaireStructureRules.CheckForPublish(q.Groups, preguntas));
            EnsureStructure(PublishedLinkIds.Conflicts(PublishedLinkIds.From(q), preguntas));

            var nextVersionNumber = (q.Versions?.Count ?? 0) + 1;

            // Un único timestamp para el recurso congelado, la versión y el cuestionario: deja
            // coherente el "última modificación" del listado con la fecha de publicación.
            var now = DateTime.UtcNow;
            var fhirJson = FhirQuestionnaireBuilder.Build(q, nextVersionNumber, now);

            var validation = _validator.Validate(fhirJson, FhirQuestionnaireBuilder.ProfileUrl);
            if (!validation.IsValid)
                throw new ValidationFailedException(validation.Errors);

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
            _access.EnsureCan(Permissions.QuestionnaireWrite);
            var source = await _repo.GetAsync(id);
            if (source == null) throw new NotFoundException("El cuestionario no existe.");
            _guard.EnsureAllows(source.SpecialtyCode);

            var variableIds = AllQuestions(source).Where(x => x.VariableId.HasValue).Select(x => x.VariableId.Value).Distinct();
            var inactivas = (await _variables.GetManyAsync(variableIds)).Where(v => !v.IsActive).ToList();
            if (inactivas.Count > 0)
                throw new ValidationFailedException(inactivas.Select(v =>
                    "La variable '" + v.Name + "' está inactiva: no se puede usar en un cuestionario nuevo."));

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
                CodingJson = source.CodingJson,
                // Se copia la revisión del origen y NO la vigente: el clon nace igual a lo que
                // se clonó. Si la variable cambió en el medio, el editor lo avisa en los dos.
                VariableId = source.VariableId,
                VariableVersionId = source.VariableVersionId,
                VariableRevision = source.VariableRevision
            };
            foreach (var opt in (source.Options ?? new List<AnswerOption>()).OrderBy(o => o.OrderIndex))
                copy.Options.Add(new AnswerOption
                {
                    Value = opt.Value,
                    Display = opt.Display,
                    Code = opt.Code,
                    SnomedCode = opt.SnomedCode,
                    SnomedDisplay = opt.SnomedDisplay,
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
            _access.EnsureCan(Permissions.QuestionnaireDelete);
            var q = await _repo.GetAsync(id);
            if (q == null) throw new NotFoundException("El cuestionario no existe.");
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
            _access.EnsureCan(Permissions.QuestionnaireWrite);
            var q = await _repo.GetAsync(id);
            if (q == null) throw new NotFoundException("El cuestionario no existe.");
            _guard.EnsureAllows(q.SpecialtyCode);
            if (q.Status == QuestionnaireStatus.Retired)
                throw new BusinessRuleException("El cuestionario ya está archivado.");
            q.Status = QuestionnaireStatus.Retired;
            q.UpdatedAt = DateTime.UtcNow;
            q.ConcurrencyVersion++;
            await _repo.UpdateAsync(q);
            _audit.Enqueue(AuditEventBuilder.For("update", "Questionnaire/" + q.Id + "$archive", userId));
        }
    }
}
