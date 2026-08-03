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
            r.FhirJson = FhirQuestionnaireResponseBuilder.Build(
                r, FhirQuestionnaireReader.Read(pinned?.FhirJson), DemographicsFrom(r.FhirJson));

            var validation = _validator.Validate(r.FhirJson);
            if (!validation.IsValid)
            {
                r.Status = ResponseStatus.InProgress;   // no se finaliza nada inválido
                throw new InvalidOperationException("Validation failed: " + string.Join("; ", validation.Errors));
            }

            r.LastSavedAt = now;
            ClearLock(r);   // ya no hay nada que seguir editando ni que bloquear
            r.ConcurrencyVersion++;
            await _repo.UpdateAsync(r);
            _audit.Enqueue(AuditEventBuilder.For("update", "QuestionnaireResponse/" + r.Id + "$finalize", userId));
            return r;
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
        public async Task ReleaseLockAsync(Guid id, string userId, Guid lockToken)
        {
            var r = await _repo.GetAsync(id);
            if (r == null) throw new InvalidOperationException("Not found");
            if (r.LockToken != lockToken || r.LockedByUserId != userId) return;

            ClearLock(r);
            r.ConcurrencyVersion++;
            await _repo.UpdateAsync(r);
            _audit.Enqueue(AuditEventBuilder.For("update", "QuestionnaireResponse/" + r.Id + "$unlock", userId));
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

            await _repo.SaveProgressAsync(r, incoming);
            _audit.Enqueue(AuditEventBuilder.For("update", "QuestionnaireResponse/" + r.Id + "$save", userId));
            return r;
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
