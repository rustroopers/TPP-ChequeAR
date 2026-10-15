using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Exceptions;
using ChequeAR.Application.Fhir;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Security;
using ChequeAR.Application.Validation;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using Newtonsoft.Json;

namespace ChequeAR.Application.Services
{
    public class QuestionnaireResponseService
    {
        // Inactividad tras la cual el lock de edición se libera solo. Se renueva en cada
        // guardado parcial y en cada $renew-lock explícito.
        private static readonly TimeSpan LockTtl = TimeSpan.FromMinutes(15);

        private readonly IQuestionnaireResponseRepository _repo;
        private readonly IQuestionnaireRepository _qRepo;
        private readonly IFhirValidator _validator;
        private readonly AuditDispatcher _audit;
        private readonly ISpecialtyGuard _guard;
        private readonly IAccessGuard _access;
        private readonly IAttachmentStore _attachments;
        private readonly IPatientDemographicsSource _demographics;

        public QuestionnaireResponseService(
            IQuestionnaireResponseRepository repo,
            IQuestionnaireRepository qRepo,
            IFhirValidator validator,
            AuditDispatcher audit,
            ISpecialtyGuard guard,
            IAccessGuard access,
            IAttachmentStore attachments,
            IPatientDemographicsSource demographics = null)
        {
            _demographics = demographics;
            _repo = repo;
            _qRepo = qRepo;
            _validator = validator;
            _audit = audit;
            _guard = guard;
            _access = access;
            _attachments = attachments;
        }

        /// <summary>
        /// Solo adjuntos subidos por quien guarda o que ya estaban en la ficha: la descarga se
        /// autoriza por las fichas que referencian el adjunto.
        /// </summary>
        private async Task EnsureAttachmentsOwnedAsync(
            IEnumerable<QuestionnaireResponseItem> incoming, IEnumerable<QuestionnaireResponseItem> existing, string userId)
        {
            var yaEstaban = new HashSet<Guid>((existing ?? Enumerable.Empty<QuestionnaireResponseItem>())
                .Where(i => i.AttachmentId.HasValue).Select(i => i.AttachmentId.Value));
            var nuevos = (incoming ?? Enumerable.Empty<QuestionnaireResponseItem>())
                .Where(i => i.AttachmentId.HasValue && !yaEstaban.Contains(i.AttachmentId.Value))
                .Select(i => i.AttachmentId.Value).Distinct();

            foreach (var id in nuevos)
            {
                var meta = await _attachments.GetAsync(id);
                if (meta == null || !string.Equals(meta.UploadedBy, userId, StringComparison.OrdinalIgnoreCase))
                    throw new ValidationFailedException(new List<string> { "Uno de los adjuntos no está disponible. Volvé a adjuntar el archivo." });
            }
        }

        // ── Lecturas ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Null si no existe. Lanza SpecialtyForbiddenException si existe pero la especialidad
        /// del cuestionario al que pertenece está fuera del scope (el controller lo traduce a
        /// 404, mismo criterio que QuestionnaireService.GetAsync).
        /// </summary>
        public async Task<QuestionnaireResponse> GetAsync(Guid id)
        {
            var r = await _repo.GetAsync(id);
            if (r == null) return null;
            await EnsureAllowsQuestionnaireAsync(r.QuestionnaireId);
            return r;
        }

        public Task<IReadOnlyList<QuestionnaireResponse>> SearchAsync(
            string patientHospitalId, Guid? questionnaireId, ResponseStatus? status, int skip, int take)
            => _repo.SearchAsync(patientHospitalId, questionnaireId, status, skip, take, _guard.Scope);

        /// <summary>Total sin paginar de <see cref="SearchAsync"/>, con los mismos filtros y el mismo scope.</summary>
        public Task<int> CountAsync(string patientHospitalId, Guid? questionnaireId, ResponseStatus? status)
            => _repo.CountAsync(patientHospitalId, questionnaireId, status, _guard.Scope);

        public Task<bool> HasResponsesAsync(Guid questionnaireId) => _repo.HasResponsesAsync(questionnaireId);

        /// <summary>Chequea el scope contra la especialidad del Questionnaire dado, sin traer el agregado completo.</summary>
        private async Task EnsureAllowsQuestionnaireAsync(Guid questionnaireId)
        {
            var code = await _qRepo.GetSpecialtyCodeAsync(questionnaireId);
            _guard.EnsureAllows(code);
        }

        private const string NotInProgressMessage =
            "Esta ficha ya no está en curso: probablemente otro usuario la finalizó o la descartó.";

        /// <summary>La ficha, si existe y su cuestionario está dentro del scope del usuario.</summary>
        private async Task<QuestionnaireResponse> LoadAsync(Guid id)
        {
            var r = await _repo.GetAsync(id);
            if (r == null) throw new NotFoundException("La ficha no existe.");
            await EnsureAllowsQuestionnaireAsync(r.QuestionnaireId);
            return r;
        }

        /// <summary><see cref="LoadAsync"/> para escribirla: exige además el permiso de carga de fichas.</summary>
        private async Task<QuestionnaireResponse> LoadForWriteAsync(Guid id)
        {
            _access.EnsureCan(Permissions.EncounterWrite);
            return await LoadAsync(id);
        }

        /// <summary>
        /// Estructura de la versión a la que está anclada la ficha. Va directo a esa versión:
        /// el autosave corre cada pocos segundos y cargar el cuestionario entero (todas sus
        /// versiones, secciones, preguntas y opciones) para leer un solo JSON era desperdicio.
        /// Las fichas anteriores al anclaje (sin versión) caen a la vigente.
        /// </summary>
        private async Task<RenderableQuestionnaire> PinnedStructureAsync(QuestionnaireResponse r)
        {
            QuestionnaireVersion pinned = null;
            if (r.QuestionnaireVersionId != Guid.Empty)
                pinned = await _qRepo.GetVersionByIdAsync(r.QuestionnaireId, r.QuestionnaireVersionId);
            if (pinned == null)
                pinned = (await _qRepo.GetAsync(r.QuestionnaireId))?.VersionOrCurrent(r.QuestionnaireVersionId);
            return FhirQuestionnaireReader.Read(pinned?.FhirJson);
        }

        /// <summary><see cref="LoadForWriteAsync"/> que además tiene que seguir en curso.</summary>
        private async Task<QuestionnaireResponse> LoadInProgressForWriteAsync(Guid id)
        {
            var r = await LoadForWriteAsync(id);
            if (r.Status != ResponseStatus.InProgress) throw new BusinessRuleException(NotInProgressMessage);
            return r;
        }

        public async Task<QuestionnaireResponse> CreateAsync(QuestionnaireResponse r, string userId)
        {
            _access.EnsureCan(Permissions.EncounterWrite);
            if (string.IsNullOrEmpty(r.PatientHospitalId))
                throw new ValidationFailedException(new List<string> { "Falta el paciente de la ficha (PatientHospitalId)." });

            var q = await _qRepo.GetAsync(r.QuestionnaireId);
            if (q == null) throw new BusinessRuleException("El cuestionario de la ficha no existe.");
            _guard.EnsureAllows(q.SpecialtyCode);
            if (q.Status == QuestionnaireStatus.Retired)
                throw new BusinessRuleException("El cuestionario está archivado: no admite fichas nuevas.");

            // Anclaje de versión: la ficha queda atada a la versión con la que se completó,
            // aunque se publique otra en el medio. El cliente manda la que rendirizó; si no
            // la manda, se cae a la vigente. Sin versión publicada no hay nada que responder.
            var pinned = q.VersionOrCurrent(r.QuestionnaireVersionId);
            if (pinned == null)
                throw new BusinessRuleException("El cuestionario no tiene ninguna versión publicada para responder.");
            r.QuestionnaireVersionId = pinned.Id;

            await EnsureAttachmentsOwnedAsync(r.Items, null, userId);

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
            var structure = FhirQuestionnaireReader.Read(pinned.FhirJson);
            OptionCodes.Normalize(structure, r.Items);
            ResponseValidator.NormalizeColumns(structure, r.Items);
            EnsureValid(structure, r.Items, complete: false);
            r.FhirJson = FhirQuestionnaireResponseBuilder.Build(r, structure, await PatientAsync(r));

            await _repo.AddAsync(r);
            // "Respuesta iniciada": marca única de que se abrió la ficha. Lo que se completa
            // después se audita por sesión (al guardar avance o finalizar), no acá.
            _audit.Enqueue(AuditEventBuilder.For("create", "QuestionnaireResponse/" + r.Id, userId));
            return r;
        }

        /// <summary>
        /// El paciente embebido en la ficha: el que ya tenía o, si no, el del hospital. Cuando el
        /// hospital no responde, ese Patient es lo único que dice de quién es la ficha (modo
        /// degradado de PatientService). Al finalizar se vuelve a pedir, por si la ficha se
        /// creó con el hospital caído.
        /// </summary>
        private async Task<PatientDemographicsDto> PatientAsync(QuestionnaireResponse r)
        {
            var embebido = FhirQuestionnaireResponseBuilder.ContainedPatient(r.FhirJson);
            if (embebido != null || _demographics == null) return embebido;
            return await _demographics.TryGetAsync(r.PatientHospitalId);
        }

        /// <param name="lockToken">
        /// Token de la sesión de edición que se cierra con esta finalización. Exigirlo
        /// evita que alguien termine (y le cierre la puerta al lock) una ficha que en
        /// realidad está completando otro usuario en este momento.
        /// </param>
        public async Task<QuestionnaireResponse> FinalizeAsync(Guid id, string userId, Guid lockToken, ICollection<QuestionnaireResponseItem> sessionBaseline = null)
        {
            var r = await LoadForWriteAsync(id);
            if (r.Status == ResponseStatus.Completed)
                return r;

            var now = DateTime.UtcNow;
            EnsureLockOwnedOrRenew(r, userId, lockToken, now);

            // Se regenera con status completed antes de validar: el recurso guardado al
            // crear dice "in-progress" y quedaría contradiciendo el estado final.
            r.Status = ResponseStatus.Completed;
            var structure = await PinnedStructureAsync(r);
            OptionCodes.Normalize(structure, r.Items);
            ResponseValidator.NormalizeColumns(structure, r.Items);

            // Una respuesta cargada cuando la pregunta estaba visible no puede quedar en la ficha
            // finalizada si la condición dejó de cumplirse (FHIR: un ítem deshabilitado no tiene
            // respuestas). Va antes de todo lo demás: la validación, el recurso FHIR, la foto de
            // auditoría y el diff de la sesión tienen que ver la ficha ya limpia.
            EnableWhenEvaluator.ClearHiddenAnswers(structure, r.Items);
            EnableWhenEvaluator.ClearUnknownAnswers(structure, r.Items);

            // Obligatorias visibles: el renderer ya las exige, pero esa validación vive en el
            // navegador y una llamada directa a $finalize la saltearía.
            var missing = EnableWhenEvaluator.MissingRequired(structure, r.Items);
            var invalid = ResponseValidator.Validate(structure, r.Items, complete: true);
            if (missing.Count > 0 || invalid.Count > 0)
            {
                r.Status = ResponseStatus.InProgress;
                throw new ValidationFailedException(missing.Select(q =>
                    "Falta responder la pregunta obligatoria \"" + (string.IsNullOrWhiteSpace(q.Text) ? q.LinkId : q.Text) + "\".")
                    .Concat(invalid));
            }

            r.FhirJson = FhirQuestionnaireResponseBuilder.Build(r, structure, await PatientAsync(r));

            var validation = _validator.Validate(r.FhirJson);
            if (!validation.IsValid)
            {
                r.Status = ResponseStatus.InProgress;   // no se finaliza nada inválido
                throw new ValidationFailedException(validation.Errors);
            }

            // Foto completa de la ficha terminada (todas las preguntas + su respuesta final):
            // la auditoría de la finalización muestra el cuestionario entero, no sólo lo que
            // cambió en la última sesión. Se guarda en el evento para que sea un registro
            // inmutable, independiente de la ficha viva.
            var snapshot = BuildFullSnapshot(structure, r.Items);

            r.LastSavedAt = now;
            r.CompletedAt = now;
            r.ReleaseLock();   // ya no hay nada que seguir editando ni que bloquear
            r.ConcurrencyVersion++;
            await _repo.UpdateAsync(r);

            // Si el cliente mandó el baseline de la sesión, se audita ADEMÁS el diff de lo que
            // se completó/cambió en esta última tanda (mismo evento "$save" que "Guardar
            // avance"). Sin esto, finalizar sólo dejaba la foto completa y el Detalle no mostraba
            // que un campo pasó de A a B en la sesión final. Va antes que el "$finalize" para que
            // en el historial (orden descendente) la finalización quede por encima del diff.
            if (sessionBaseline != null)
            {
                var sessionChanges = BuildNetDiff(sessionBaseline, r.Items, structure);
                if (sessionChanges.Count > 0)
                {
                    var saveEvt = AuditEventBuilder.For("update", "QuestionnaireResponse/" + r.Id + "$save", userId);
                    saveEvt.FhirJson = JsonConvert.SerializeObject(sessionChanges);
                    await _audit.PersistImmediatelyAsync(saveEvt);
                }
            }

            var evt = AuditEventBuilder.For("update", "QuestionnaireResponse/" + r.Id + "$finalize", userId);
            if (snapshot.Count > 0) evt.FhirJson = JsonConvert.SerializeObject(snapshot);
            // Persistencia inmediata (no vía el flush por timer): al finalizar, la persona
            // suele abrir el "Detalle" de la ficha en el acto, y con el flush diferido el
            // evento recién guardado no llegaba a aparecer.
            await _audit.PersistImmediatelyAsync(evt);
            return r;
        }

        /// <summary>
        /// Descarta una ficha InProgress sin terminar de completar (ej. el médico prefiere
        /// arrancarla de cero en vez de retomar un borrador viejo). Queda marcada
        /// EnteredInError -- no se borra, para no perder el rastro de auditoría -- y libera
        /// el lock si lo tenía. Mismo criterio de "lock libre u ocupado por este mismo
        /// usuario" que AcquireLockAsync: no hace falta haber tomado el lock antes para
        /// descartar, pero si otro médico está editando en este momento, se rechaza.
        /// </summary>
        public async Task<QuestionnaireResponse> AbandonAsync(Guid id, string userId)
        {
            var r = await LoadInProgressForWriteAsync(id);

            var now = DateTime.UtcNow;
            if (!r.CanBeLockedBy(userId, now))
                throw new LockConflictException(r.LockedByUserId, r.LockExpiresAt);

            r.Status = ResponseStatus.EnteredInError;
            r.FhirJson = FhirQuestionnaireResponseBuilder.Build(
                r, await PinnedStructureAsync(r), FhirQuestionnaireResponseBuilder.ContainedPatient(r.FhirJson));

            r.ReleaseLock();
            r.LastSavedAt = now;
            r.ConcurrencyVersion++;
            await _repo.UpdateAsync(r);

            _audit.Enqueue(AuditEventBuilder.For("update", "QuestionnaireResponse/" + r.Id + "$abandon", userId));
            return r;
        }

        /// <summary>
        /// Vista previa de re-anclar la ficha a la versión vigente del cuestionario: qué
        /// respuestas se conservan, qué preguntas nuevas aparecen y qué respuestas se
        /// descartarían. No modifica nada. Sirve para que el médico decida si actualiza.
        /// </summary>
        public async Task<RetargetPreview> BuildRetargetPreviewAsync(Guid id)
        {
            var r = await LoadAsync(id);

            var q = await _qRepo.GetAsync(r.QuestionnaireId);
            var pinned = q?.VersionOrCurrent(r.QuestionnaireVersionId);
            var current = q?.CurrentVersion();
            var preview = new RetargetPreview
            {
                FromVersion = pinned?.VersionNumber ?? 0,
                ToVersion = current?.VersionNumber ?? 0
            };
            if (pinned == null || current == null || pinned.Id == current.Id)
            {
                preview.AlreadyCurrent = true;
                return preview;
            }

            var pinnedStructure = FhirQuestionnaireReader.Read(pinned.FhirJson);
            var currentStructure = FhirQuestionnaireReader.Read(current.FhirJson);
            var pinnedTexts = FlattenTexts(pinnedStructure);
            var currentTexts = FlattenTexts(currentStructure);
            var answered = AnsweredValues(r.Items, pinnedStructure);
            var copies = (r.Items ?? new List<QuestionnaireResponseItem>())
                .Select(i => new QuestionnaireResponseItem { LinkId = i.LinkId, ValueCode = i.ValueCode }).ToList();
            var orphans = new HashSet<string>(CarryOptions(pinnedStructure, currentStructure, copies));

            foreach (var cur in currentTexts)
            {
                if (orphans.Contains(cur.Key) && answered.TryGetValue(cur.Key, out var lost))
                    preview.Dropped.Add(new RetargetItem { LinkId = cur.Key, Text = cur.Value, Answer = lost });
                else if (pinnedTexts.TryGetValue(cur.Key, out var oldText))
                    preview.Carried.Add(new RetargetItem { LinkId = cur.Key, Text = cur.Value, Changed = oldText != cur.Value });
                else
                    preview.Added.Add(new RetargetItem { LinkId = cur.Key, Text = cur.Value });
            }
            foreach (var old in pinnedTexts)
            {
                if (currentTexts.ContainsKey(old.Key)) continue;      // no desaparece
                if (!answered.TryGetValue(old.Key, out var val)) continue;   // no había respuesta que perder
                preview.Dropped.Add(new RetargetItem { LinkId = old.Key, Text = old.Value, Answer = val });
            }
            return preview;
        }

        /// <summary>
        /// Re-ancla la ficha a la versión vigente del cuestionario: conserva las respuestas de
        /// las preguntas que siguen existiendo y descarta las de las eliminadas (el frontend
        /// ya se lo confirmó al médico con <see cref="BuildRetargetPreviewAsync"/>). Requiere
        /// el lock de edición, igual que finalizar. Deja un evento de auditoría con lo
        /// descartado.
        /// </summary>
        public async Task<QuestionnaireResponse> RetargetToCurrentVersionAsync(Guid id, string userId, Guid lockToken)
        {
            var r = await LoadInProgressForWriteAsync(id);

            var now = DateTime.UtcNow;
            EnsureLockOwnedOrRenew(r, userId, lockToken, now);

            var q = await _qRepo.GetAsync(r.QuestionnaireId);
            var current = q?.CurrentVersion();
            if (current == null) throw new BusinessRuleException("El cuestionario no tiene ninguna versión publicada.");
            if (current.Id == r.QuestionnaireVersionId) return r;   // ya está en la vigente

            var pinned = q?.VersionOrCurrent(r.QuestionnaireVersionId);
            var pinnedStructure = FhirQuestionnaireReader.Read(pinned?.FhirJson);
            var currentStructure = FhirQuestionnaireReader.Read(current.FhirJson);
            var currentLinkIds = new HashSet<string>(FlattenTexts(currentStructure).Keys);

            // Lo que se descarta (para el registro de auditoría): preguntas respondidas que la
            // versión nueva ya no tiene, y opciones elegidas que ya no existen en ella.
            var dropped = new List<ResponseFieldChange>();
            var answered = AnsweredValues(r.Items, pinnedStructure);
            var pinnedTexts = FlattenTexts(pinnedStructure);
            var orphans = new HashSet<string>(CarryOptions(pinnedStructure, currentStructure, r.Items));
            ResponseValidator.NormalizeColumns(currentStructure, r.Items);
            foreach (var ans in answered)
            {
                if (currentLinkIds.Contains(ans.Key) && !orphans.Contains(ans.Key)) continue;
                dropped.Add(new ResponseFieldChange
                {
                    LinkId = ans.Key,
                    Text = pinnedTexts.TryGetValue(ans.Key, out var t) && !string.IsNullOrWhiteSpace(t) ? t : ans.Key,
                    Before = ans.Value,
                    After = currentLinkIds.Contains(ans.Key)
                        ? ResponseAnswerFormatter.Format(r.Items.Where(i => i.LinkId == ans.Key), FindQuestion(currentStructure, ans.Key))
                        : ResponseAnswerFormatter.Empty   // descartada al pasar de versión
                });
            }

            // FhirJson con la estructura nueva y sólo los items que se conservan.
            var keptItems = (r.Items ?? new List<QuestionnaireResponseItem>())
                .Where(i => currentLinkIds.Contains(i.LinkId)).ToList();
            var mergedForFhir = WithItems(r, keptItems, current.Id);
            r.FhirJson = FhirQuestionnaireResponseBuilder.Build(mergedForFhir, currentStructure, FhirQuestionnaireResponseBuilder.ContainedPatient(r.FhirJson));
            r.QuestionnaireVersionId = current.Id;
            r.LastSavedAt = now;
            r.ConcurrencyVersion++;

            await _repo.RetargetAsync(r, currentLinkIds.ToList());

            var evt = AuditEventBuilder.For("update", "QuestionnaireResponse/" + r.Id + "$retarget", userId);
            if (dropped.Count > 0) evt.FhirJson = JsonConvert.SerializeObject(dropped);
            // Se persiste en el acto: es el único registro de las respuestas descartadas.
            await _audit.PersistImmediatelyAsync(evt);
            return r;
        }

        /// <summary>
        /// Lleva las respuestas de opción a los códigos de la versión nueva y vacía las que no
        /// tienen equivalente en ella. Devuelve los linkId que perdieron alguna opción.
        /// </summary>
        private static List<string> CarryOptions(
            RenderableQuestionnaire from, RenderableQuestionnaire to, ICollection<QuestionnaireResponseItem> items)
        {
            OptionCodes.Carry(from, to, items);
            return OptionCodes.ClearOrphans(to, items);
        }

        /// <summary>linkId -> texto de cada pregunta (no grupos) de una estructura, en orden.</summary>
        private static Dictionary<string, string> FlattenTexts(RenderableQuestionnaire structure)
        {
            var texts = new Dictionary<string, string>();
            if (structure == null) return texts;
            foreach (var g in structure.Groups)
                foreach (var question in g.Questions)
                    if (!string.IsNullOrEmpty(question.LinkId) && !texts.ContainsKey(question.LinkId))
                        texts[question.LinkId] = question.Text;
            return texts;
        }

        private static RenderableQuestion FindQuestion(RenderableQuestionnaire structure, string linkId)
            => structure?.Groups.SelectMany(g => g.Questions).FirstOrDefault(x => x.LinkId == linkId);

        /// <summary>linkId -> respuesta formateada, sólo para las preguntas que tienen algún valor.</summary>
        private static Dictionary<string, string> AnsweredValues(
            ICollection<QuestionnaireResponseItem> items, RenderableQuestionnaire structure)
        {
            var result = new Dictionary<string, string>();
            foreach (var gr in (items ?? new List<QuestionnaireResponseItem>()).GroupBy(i => i.LinkId ?? string.Empty))
            {
                var formatted = ResponseAnswerFormatter.Format(gr.ToList(), FindQuestion(structure, gr.Key));
                if (formatted != ResponseAnswerFormatter.Empty) result[gr.Key] = formatted;
            }
            return result;
        }

        /// <summary>
        /// Toma el lock de edición si está libre (nadie lo tiene, venció, o ya lo tiene
        /// este mismo usuario). Si lo tiene otro usuario y sigue vigente, se rechaza: es
        /// justamente el caso que evita que dos médicos completen la misma ficha a la vez.
        /// </summary>
        public async Task<QuestionnaireResponse> AcquireLockAsync(Guid id, string userId)
        {
            var r = await LoadInProgressForWriteAsync(id);

            var now = DateTime.UtcNow;
            if (!r.CanBeLockedBy(userId, now))
                throw new LockConflictException(r.LockedByUserId, r.LockExpiresAt);

            r.AcquireLock(userId, now, LockTtl);
            r.ConcurrencyVersion++;
            await _repo.UpdateAsync(r);
            // "Edición iniciada": deja traza de cada acceso de edición (quién abrió la ficha y
            // cuándo). Lo que se completa en esa sesión se audita aparte, al cerrarla.
            _audit.Enqueue(AuditEventBuilder.For("update", "QuestionnaireResponse/" + r.Id + "$lock", userId));
            return r;
        }

        /// <summary>Extiende el vencimiento del lock vigente (heartbeat / aviso de "1 minuto").</summary>
        public async Task<QuestionnaireResponse> RenewLockAsync(Guid id, string userId, Guid lockToken)
        {
            var r = await LoadForWriteAsync(id);
            EnsureLockOwnedOrRenew(r, userId, lockToken, DateTime.UtcNow);
            r.ConcurrencyVersion++;
            await _repo.UpdateAsync(r);
            return r;
        }

        /// <summary>
        /// Libera el lock. Idempotente a propósito: si ya no es de este usuario (se lo
        /// ganó otro, o ya estaba libre) no es un error, simplemente no hay nada que hacer.
        /// </summary>
        /// <param name="sessionBaseline">
        /// Respuestas al abrir la sesión (las manda el cliente en el "Guardar avance"). Si
        /// viene y hubo cambios netos, se audita UN evento con el diff de lo completado. Si es
        /// null (cierre pasivo de pestaña) sólo se libera el lock, sin auditar: los datos ya
        /// quedaron por autosave y no fue un guardado deliberado.
        /// </param>
        public async Task ReleaseLockAsync(
            Guid id, string userId, Guid lockToken, ICollection<QuestionnaireResponseItem> sessionBaseline = null)
        {
            var r = await LoadForWriteAsync(id);
            if (!r.IsLockHeldBy(userId, lockToken)) return;

            List<ResponseFieldChange> changes = null;
            if (sessionBaseline != null)
            {
                changes = BuildNetDiff(sessionBaseline, r.Items, await PinnedStructureAsync(r));
            }

            r.ReleaseLock();
            r.ConcurrencyVersion++;
            await _repo.UpdateAsync(r);

            // Sólo se audita un "guardado de avance" si el cliente mandó el baseline (acción
            // deliberada) y realmente cambió algo en la sesión.
            if (changes != null && changes.Count > 0)
            {
                var evt = AuditEventBuilder.For("update", "QuestionnaireResponse/" + r.Id + "$save", userId);
                evt.FhirJson = JsonConvert.SerializeObject(changes);
                // Inmediato, no diferido: el "Detalle" de la ficha se abre apenas se guarda el
                // avance, y con el flush por timer el evento no alcanzaba a estar persistido.
                await _audit.PersistImmediatelyAsync(evt);
            }
        }

        /// <summary>
        /// Guardado parcial (autosave de una llamada/sesión): mergea los items entrantes
        /// por LinkId sin exigir que el cuestionario esté completo, y deja el estado en
        /// InProgress. Requiere lock vigente (con grace period) y, si se manda, la
        /// ConcurrencyVersion esperada.
        /// </summary>
        public async Task<QuestionnaireResponse> SaveProgressAsync(
            Guid id, string userId, Guid lockToken, int? expectedVersion, ICollection<QuestionnaireResponseItem> items)
        {
            var r = await LoadInProgressForWriteAsync(id);

            var now = DateTime.UtcNow;
            EnsureLockOwnedOrRenew(r, userId, lockToken, now);

            // Chequeo temprano, igual criterio que QuestionnaireService.UpdateAsync: la
            // carrera real entre este read y el write la ataja el ConcurrencyToken del UPDATE.
            if (expectedVersion.HasValue && r.ConcurrencyVersion != expectedVersion.Value)
                throw new ConcurrencyConflictException(expectedVersion.Value, r.ConcurrencyVersion);

            var incoming = (items ?? new List<QuestionnaireResponseItem>()).ToList();
            await EnsureAttachmentsOwnedAsync(incoming, r.Items, userId);
            var touchedLinkIds = new HashSet<string>(incoming.Select(i => i.LinkId));
            foreach (var item in incoming)
            {
                if (item.Id == Guid.Empty) item.Id = Guid.NewGuid();
                item.ResponseId = r.Id;
            }

            // El FhirJson se arma con la foto YA mergeada (lo que se conserva + lo nuevo),
            // pero sin tocar todavía r.Items -- el merge real contra la base lo hace el
            // repo, que sabe cómo borrar los items viejos sin violar la FK requerida.
            var keptItems = (r.Items ?? new List<QuestionnaireResponseItem>())
                .Where(i => !touchedLinkIds.Contains(i.LinkId)).ToList();
            var structure = await PinnedStructureAsync(r);
            OptionCodes.Normalize(structure, incoming);
            ResponseValidator.NormalizeColumns(structure, incoming);
            EnsureValid(structure, incoming, complete: false);
            var mergedForFhir = WithItems(r, keptItems.Concat(incoming).ToList(), r.QuestionnaireVersionId);
            r.FhirJson = FhirQuestionnaireResponseBuilder.Build(
                mergedForFhir, structure, FhirQuestionnaireResponseBuilder.ContainedPatient(r.FhirJson));

            r.LastSavedAt = now;
            r.IsAutosave = true;
            r.ConcurrencyVersion++;

            // El autosave NO audita: dispararía un evento cada pocos segundos. El diff de lo
            // completado se registra una sola vez por sesión, al cerrarla (guardar avance o
            // finalizar), comparando contra el baseline con el que se abrió.
            await _repo.SaveProgressAsync(r, incoming);
            return r;
        }

        /// <summary>
        /// Copia de la cabecera con otros ítems, para armar el recurso FHIR antes de que el repo
        /// toque los de la entidad. Lleva la versión anclada: sin ella el recurso sale sin anclaje.
        /// </summary>
        private static QuestionnaireResponse WithItems(
            QuestionnaireResponse r, ICollection<QuestionnaireResponseItem> items, Guid versionId)
            => new QuestionnaireResponse
            {
                Id = r.Id,
                QuestionnaireId = r.QuestionnaireId,
                QuestionnaireVersionId = versionId,
                EncounterId = r.EncounterId,
                Status = r.Status,
                AuthoredAt = r.AuthoredAt,
                PatientHospitalId = r.PatientHospitalId,
                AuthorUserId = r.AuthorUserId,
                Items = items
            };

        private static void EnsureValid(
            RenderableQuestionnaire structure, IEnumerable<QuestionnaireResponseItem> items, bool complete)
        {
            var errors = ResponseValidator.Validate(structure, items, complete);
            if (errors.Count > 0) throw new ValidationFailedException(errors);
        }

        /// <summary>
        /// Foto completa de la ficha: todas las secciones y preguntas de la versión anclada,
        /// con su respuesta final ("—" si quedó sin responder). Preserva el orden del
        /// cuestionario. Se usa para el registro de auditoría de la finalización.
        /// </summary>
        private static List<ResponseSnapshotSection> BuildFullSnapshot(
            RenderableQuestionnaire structure, ICollection<QuestionnaireResponseItem> items)
        {
            var sections = new List<ResponseSnapshotSection>();
            if (structure == null) return sections;

            var byLink = (items ?? new List<QuestionnaireResponseItem>())
                .GroupBy(i => i.LinkId ?? string.Empty)
                .ToDictionary(gr => gr.Key, gr => gr.ToList());

            foreach (var g in structure.Groups)
            {
                var section = new ResponseSnapshotSection { Title = g.Title };
                foreach (var question in g.Questions)
                {
                    var key = question.LinkId ?? string.Empty;
                    section.Answers.Add(new ResponseSnapshotAnswer
                    {
                        Text = string.IsNullOrWhiteSpace(question.Text) ? question.LinkId : question.Text,
                        Answer = ResponseAnswerFormatter.Format(byLink.TryGetValue(key, out var its) ? its : null, question)
                    });
                }
                if (section.Answers.Count > 0) sections.Add(section);
            }
            return sections;
        }

        /// <summary>
        /// Diff neto de una sesión de edición: compara las respuestas al abrir (baseline)
        /// contra las que quedaron al cerrar (current), sobre la unión de ambos conjuntos de
        /// preguntas. Es lo que se completó/cambió en toda la sesión, sin importar cuántos
        /// autosaves hubo en el medio. Devuelve lista vacía si no hubo cambios netos.
        /// </summary>
        private static List<ResponseFieldChange> BuildNetDiff(
            ICollection<QuestionnaireResponseItem> baseline,
            ICollection<QuestionnaireResponseItem> current,
            RenderableQuestionnaire structure)
        {
            var before = (baseline ?? new List<QuestionnaireResponseItem>()).ToList();
            var after = (current ?? new List<QuestionnaireResponseItem>()).ToList();
            var linkIds = new HashSet<string>(before.Select(i => i.LinkId).Concat(after.Select(i => i.LinkId)));
            return BuildSessionDiff(before, after, linkIds, structure);
        }

        /// <summary>
        /// Arma el diff comparando, para cada linkId dado, el valor de "previousItems" contra
        /// el de "incoming", incluyéndolo sólo si cambió. El texto de la pregunta sale de la
        /// versión anclada; si no está, se cae al propio linkId. Lista vacía si nada cambió.
        /// </summary>
        private static List<ResponseFieldChange> BuildSessionDiff(
            List<QuestionnaireResponseItem> previousItems,
            List<QuestionnaireResponseItem> incoming,
            HashSet<string> touchedLinkIds,
            RenderableQuestionnaire structure)
        {
            var texts = FlattenTexts(structure);

            var beforeByLink = previousItems.GroupBy(i => i.LinkId)
                .ToDictionary(gr => gr.Key ?? string.Empty, gr => gr.ToList());
            var afterByLink = incoming.GroupBy(i => i.LinkId)
                .ToDictionary(gr => gr.Key ?? string.Empty, gr => gr.ToList());

            var changes = new List<ResponseFieldChange>();
            foreach (var linkId in touchedLinkIds)
            {
                var key = linkId ?? string.Empty;
                var question = FindQuestion(structure, key);
                var before = ResponseAnswerFormatter.Format(beforeByLink.TryGetValue(key, out var b) ? b : null, question);
                var after = ResponseAnswerFormatter.Format(afterByLink.TryGetValue(key, out var a) ? a : null, question);
                if (before == after) continue;   // reguardado sin novedad
                changes.Add(new ResponseFieldChange
                {
                    LinkId = linkId,
                    Text = texts.TryGetValue(key, out var t) && !string.IsNullOrWhiteSpace(t) ? t : linkId,
                    Before = before,
                    After = after
                });
            }
            return changes;
        }


        /// <summary>
        /// Valida que quien escribe sea dueño del lock vigente, y de paso implementa el
        /// "grace period" acordado: si el lock vencido es el mismo que ya tenía este
        /// usuario (nadie más lo tomó después), se acepta igual y se renueva en silencio
        /// -- el TTL protege contra OTRO usuario pisando el trabajo, no castiga a este por
        /// tardarse. Si el token no coincide (otro usuario tomó el lock, esté vencido o
        /// no), se rechaza.
        /// </summary>
        private static void EnsureLockOwnedOrRenew(QuestionnaireResponse r, string userId, Guid lockToken, DateTime now)
        {
            if (!r.LockToken.HasValue)
                throw new BusinessRuleException("No hay una sesión de edición activa: hay que tomar el lock ($lock) antes de guardar.");
            if (!r.IsLockHeldBy(userId, lockToken))
                throw new LockConflictException(r.LockedByUserId, r.LockExpiresAt);

            r.RenewLock(now, LockTtl);
        }
    }
}
