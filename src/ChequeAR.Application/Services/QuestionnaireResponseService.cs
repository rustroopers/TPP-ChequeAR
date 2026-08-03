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
        // Inactividad tras la cual el lock de edición se libera solo. Se renueva en cada
        // guardado parcial y en cada $renew-lock explícito.
        private static readonly TimeSpan LockTtl = TimeSpan.FromMinutes(15);

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
            // "Respuesta iniciada": marca única de que se abrió la ficha. Lo que se completa
            // después se audita por sesión (al guardar avance o finalizar), no acá.
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

        /// <param name="lockToken">
        /// Token de la sesión de edición que se cierra con esta finalización. Exigirlo
        /// evita que alguien termine (y le cierre la puerta al lock) una ficha que en
        /// realidad está completando otro usuario en este momento.
        /// </param>
        public async Task<QuestionnaireResponse> FinalizeAsync(Guid id, string userId, Guid lockToken)
        {
            var r = await _repo.GetAsync(id);
            if (r == null) throw new InvalidOperationException("Not found");
            if (r.Status == ResponseStatus.Completed)
                return r;

            var now = DateTime.UtcNow;
            EnsureLockOwnedOrRenew(r, userId, lockToken, now);

            // Se regenera con status completed antes de validar: el recurso guardado al
            // crear dice "in-progress" y quedaría contradiciendo el estado final.
            r.Status = ResponseStatus.Completed;
            var questionnaire = await _qRepo.GetAsync(r.QuestionnaireId);
            var pinned = ResolveVersion(questionnaire, r.QuestionnaireVersionId);
            var structure = FhirQuestionnaireReader.Read(pinned?.FhirJson);
            r.FhirJson = FhirQuestionnaireResponseBuilder.Build(r, structure, DemographicsFrom(r.FhirJson));

            var validation = _validator.Validate(r.FhirJson);
            if (!validation.IsValid)
            {
                r.Status = ResponseStatus.InProgress;   // no se finaliza nada inválido
                throw new InvalidOperationException("Validation failed: " + string.Join("; ", validation.Errors));
            }

            // Foto completa de la ficha terminada (todas las preguntas + su respuesta final):
            // la auditoría de la finalización muestra el cuestionario entero, no sólo lo que
            // cambió en la última sesión. Se guarda en el evento para que sea un registro
            // inmutable, independiente de la ficha viva.
            var snapshot = BuildFullSnapshot(structure, r.Items);

            r.LastSavedAt = now;
            ClearLock(r);   // ya no hay nada que seguir editando ni que bloquear
            r.ConcurrencyVersion++;
            await _repo.UpdateAsync(r);
            var evt = AuditEventBuilder.For("update", "QuestionnaireResponse/" + r.Id + "$finalize", userId);
            if (snapshot.Count > 0) evt.FhirJson = JsonConvert.SerializeObject(snapshot);
            _audit.Enqueue(evt);
            return r;
        }

        /// <summary>
        /// Vista previa de re-anclar la ficha a la versión vigente del cuestionario: qué
        /// respuestas se conservan, qué preguntas nuevas aparecen y qué respuestas se
        /// descartarían. No modifica nada. Sirve para que el médico decida si actualiza.
        /// </summary>
        public async Task<RetargetPreview> BuildRetargetPreviewAsync(Guid id)
        {
            var r = await _repo.GetAsync(id);
            if (r == null) throw new InvalidOperationException("Not found");

            var q = await _qRepo.GetAsync(r.QuestionnaireId);
            var pinned = ResolveVersion(q, r.QuestionnaireVersionId);
            var current = ResolveVersion(q, Guid.Empty);
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

            var pinnedTexts = FlattenTexts(FhirQuestionnaireReader.Read(pinned.FhirJson));
            var currentTexts = FlattenTexts(FhirQuestionnaireReader.Read(current.FhirJson));
            var answered = AnsweredValues(r.Items);

            foreach (var cur in currentTexts)
            {
                if (pinnedTexts.TryGetValue(cur.Key, out var oldText))
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
            var r = await _repo.GetAsync(id);
            if (r == null) throw new InvalidOperationException("Not found");
            if (r.Status != ResponseStatus.InProgress)
                throw new InvalidOperationException("Only in-progress responses can change version");

            var now = DateTime.UtcNow;
            EnsureLockOwnedOrRenew(r, userId, lockToken, now);

            var q = await _qRepo.GetAsync(r.QuestionnaireId);
            var current = ResolveVersion(q, Guid.Empty);
            if (current == null) throw new InvalidOperationException("Questionnaire has no published version");
            if (current.Id == r.QuestionnaireVersionId) return r;   // ya está en la vigente

            var pinned = ResolveVersion(q, r.QuestionnaireVersionId);
            var currentStructure = FhirQuestionnaireReader.Read(current.FhirJson);
            var currentLinkIds = new HashSet<string>(FlattenTexts(currentStructure).Keys);

            // Lo que se descarta (para el registro de auditoría): preguntas respondidas que la
            // versión nueva ya no tiene.
            var dropped = new List<ResponseFieldChange>();
            var answered = AnsweredValues(r.Items);
            var pinnedTexts = FlattenTexts(FhirQuestionnaireReader.Read(pinned?.FhirJson));
            foreach (var ans in answered)
            {
                if (currentLinkIds.Contains(ans.Key)) continue;
                dropped.Add(new ResponseFieldChange
                {
                    LinkId = ans.Key,
                    Text = pinnedTexts.TryGetValue(ans.Key, out var t) && !string.IsNullOrWhiteSpace(t) ? t : ans.Key,
                    Before = ans.Value,
                    After = "—"   // descartada al pasar de versión
                });
            }

            // FhirJson con la estructura nueva y sólo los items que se conservan.
            var keptItems = (r.Items ?? new List<QuestionnaireResponseItem>())
                .Where(i => currentLinkIds.Contains(i.LinkId)).ToList();
            var mergedForFhir = new QuestionnaireResponse
            {
                Id = r.Id,
                QuestionnaireId = r.QuestionnaireId,
                Status = r.Status,
                AuthoredAt = r.AuthoredAt,
                PatientHospitalId = r.PatientHospitalId,
                AuthorUserId = r.AuthorUserId,
                Items = keptItems
            };
            r.FhirJson = FhirQuestionnaireResponseBuilder.Build(mergedForFhir, currentStructure, DemographicsFrom(r.FhirJson));
            r.QuestionnaireVersionId = current.Id;
            r.LastSavedAt = now;
            r.ConcurrencyVersion++;

            await _repo.RetargetAsync(r, currentLinkIds.ToList());

            var evt = AuditEventBuilder.For("update", "QuestionnaireResponse/" + r.Id + "$retarget", userId);
            if (dropped.Count > 0) evt.FhirJson = JsonConvert.SerializeObject(dropped);
            _audit.Enqueue(evt);
            return r;
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

        /// <summary>linkId -> respuesta formateada, sólo para las preguntas que tienen algún valor.</summary>
        private static Dictionary<string, string> AnsweredValues(ICollection<QuestionnaireResponseItem> items)
        {
            var result = new Dictionary<string, string>();
            foreach (var gr in (items ?? new List<QuestionnaireResponseItem>()).GroupBy(i => i.LinkId ?? string.Empty))
            {
                var formatted = FormatAnswers(gr.ToList());
                if (formatted != "—") result[gr.Key] = formatted;
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
            var r = await _repo.GetAsync(id);
            if (r == null) throw new InvalidOperationException("Not found");
            if (r.Status != ResponseStatus.InProgress)
                throw new InvalidOperationException("Only in-progress responses can be locked");

            var now = DateTime.UtcNow;
            var free = !r.LockToken.HasValue || !r.LockExpiresAt.HasValue || r.LockExpiresAt.Value < now;
            var ownedBySameUser = r.LockedByUserId == userId;
            if (!free && !ownedBySameUser)
                throw new LockConflictException(r.LockedByUserId, r.LockExpiresAt);

            r.LockToken = Guid.NewGuid();
            r.LockedByUserId = userId;
            r.LockedAt = now;
            r.LockExpiresAt = now.Add(LockTtl);
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
            var r = await _repo.GetAsync(id);
            if (r == null) throw new InvalidOperationException("Not found");
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
            var r = await _repo.GetAsync(id);
            if (r == null) throw new InvalidOperationException("Not found");
            if (r.LockToken != lockToken || r.LockedByUserId != userId) return;

            List<ResponseFieldChange> changes = null;
            if (sessionBaseline != null)
            {
                var questionnaire = await _qRepo.GetAsync(r.QuestionnaireId);
                var pinned = ResolveVersion(questionnaire, r.QuestionnaireVersionId);
                changes = BuildNetDiff(sessionBaseline, r.Items, FhirQuestionnaireReader.Read(pinned?.FhirJson));
            }

            ClearLock(r);
            r.ConcurrencyVersion++;
            await _repo.UpdateAsync(r);

            // Sólo se audita un "guardado de avance" si el cliente mandó el baseline (acción
            // deliberada) y realmente cambió algo en la sesión.
            if (changes != null && changes.Count > 0)
            {
                var evt = AuditEventBuilder.For("update", "QuestionnaireResponse/" + r.Id + "$save", userId);
                evt.FhirJson = JsonConvert.SerializeObject(changes);
                _audit.Enqueue(evt);
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
            var r = await _repo.GetAsync(id);
            if (r == null) throw new InvalidOperationException("Not found");
            if (r.Status != ResponseStatus.InProgress)
                throw new InvalidOperationException("Cannot save progress on a response that is not in progress");

            var now = DateTime.UtcNow;
            EnsureLockOwnedOrRenew(r, userId, lockToken, now);

            // Chequeo temprano, igual criterio que QuestionnaireService.UpdateAsync: la
            // carrera real entre este read y el write la ataja el ConcurrencyToken del UPDATE.
            if (expectedVersion.HasValue && r.ConcurrencyVersion != expectedVersion.Value)
                throw new ConcurrencyConflictException(expectedVersion.Value, r.ConcurrencyVersion);

            var incoming = (items ?? new List<QuestionnaireResponseItem>()).ToList();
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
            var questionnaire = await _qRepo.GetAsync(r.QuestionnaireId);
            var pinned = ResolveVersion(questionnaire, r.QuestionnaireVersionId);
            var mergedForFhir = new QuestionnaireResponse
            {
                Id = r.Id,
                QuestionnaireId = r.QuestionnaireId,
                Status = r.Status,
                AuthoredAt = r.AuthoredAt,
                PatientHospitalId = r.PatientHospitalId,
                AuthorUserId = r.AuthorUserId,
                Items = keptItems.Concat(incoming).ToList()
            };
            r.FhirJson = FhirQuestionnaireResponseBuilder.Build(
                mergedForFhir, FhirQuestionnaireReader.Read(pinned?.FhirJson), DemographicsFrom(r.FhirJson));

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
                        Answer = FormatAnswers(byLink.TryGetValue(key, out var its) ? its : null)
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
                var before = FormatAnswers(beforeByLink.TryGetValue(key, out var b) ? b : null);
                var after = FormatAnswers(afterByLink.TryGetValue(key, out var a) ? a : null);
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

        private static string FormatAnswers(List<QuestionnaireResponseItem> items)
        {
            if (items == null || items.Count == 0) return "—";
            var values = items.Select(FormatAnswer).Where(v => v != "—").ToList();
            return values.Count > 0 ? string.Join(", ", values) : "—";
        }

        private static string FormatAnswer(QuestionnaireResponseItem item)
        {
            if (item == null) return "—";
            if (item.ValueBoolean.HasValue) return item.ValueBoolean.Value ? "Sí" : "No";
            if (item.ValueInteger.HasValue) return item.ValueInteger.Value.ToString();
            if (item.ValueDecimal.HasValue) return item.ValueDecimal.Value.ToString();
            if (item.ValueDate.HasValue) return item.ValueDate.Value.ToString("yyyy-MM-dd");
            if (!string.IsNullOrEmpty(item.ValueString)) return item.ValueString;
            if (!string.IsNullOrEmpty(item.ValueCode)) return item.ValueCode;
            return "—";
        }

        private static void ClearLock(QuestionnaireResponse r)
        {
            r.LockToken = null;
            r.LockedByUserId = null;
            r.LockedAt = null;
            r.LockExpiresAt = null;
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
                throw new InvalidOperationException("No hay una sesión de edición activa: hay que tomar el lock ($lock) antes de guardar.");
            if (r.LockToken.Value != lockToken || r.LockedByUserId != userId)
                throw new LockConflictException(r.LockedByUserId, r.LockExpiresAt);

            r.LockExpiresAt = now.Add(LockTtl);
        }
    }
}
