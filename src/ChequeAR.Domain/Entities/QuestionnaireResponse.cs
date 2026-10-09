using System;
using System.Collections.Generic;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Domain.Entities
{
    public class QuestionnaireResponse
    {
        public Guid Id { get; set; }
        public Guid QuestionnaireId { get; set; }
        public Guid QuestionnaireVersionId { get; set; }
        public string PatientHospitalId { get; set; }
        public string EncounterId { get; set; }
        public ResponseStatus Status { get; set; }
        public DateTime AuthoredAt { get; set; }
        public string AuthorUserId { get; set; }
        public string FhirJson { get; set; }
        public DateTime LastSavedAt { get; set; }
        public bool IsAutosave { get; set; }

        /// <summary>
        /// Contador de escrituras para concurrencia optimista, mismo mecanismo que
        /// Questionnaire.ConcurrencyVersion: viaja como ETag, el cliente lo devuelve en
        /// If-Match, y el server rechaza el guardado si no coincide con el de la base.
        /// </summary>
        public int ConcurrencyVersion { get; set; }

        /// <summary>
        /// Lock pesimista de sesión de edición: mientras un médico está completando el
        /// cuestionario (ej. durante una llamada), estos 4 campos evitan que otro usuario
        /// entre a escribir a la vez. Se libera al finalizar, al llamar $unlock, o por
        /// vencimiento del TTL (LockExpiresAt) si nadie lo renovó.
        /// </summary>
        public Guid? LockToken { get; set; }
        public string LockedByUserId { get; set; }
        public DateTime? LockedAt { get; set; }
        public DateTime? LockExpiresAt { get; set; }

        public virtual ICollection<QuestionnaireResponseItem> Items { get; set; } = new List<QuestionnaireResponseItem>();

        // ── Lock de edición ───────────────────────────────────────────────────────────
        // Las reglas viven acá y no en el servicio: son invariantes de la ficha. El servicio
        // decide qué excepción lanzar cuando una regla no se cumple.

        /// <summary>Nadie tiene el lock, o el que lo tenía lo dejó vencer.</summary>
        public bool IsLockFree(DateTime now) =>
            !LockToken.HasValue || !LockExpiresAt.HasValue || LockExpiresAt.Value < now;

        /// <summary>
        /// El usuario puede tomar (o retomar) el lock: está libre, o ya es suyo. Si lo tiene
        /// OTRO usuario y sigue vigente, no: es el caso que evita que dos médicos completen
        /// la misma ficha a la vez.
        /// </summary>
        public bool CanBeLockedBy(string userId, DateTime now) => IsLockFree(now) || LockedByUserId == userId;

        /// <summary>
        /// Quien escribe es dueño de la sesión de edición. El vencimiento no importa: si nadie
        /// más tomó el lock después (el token sigue siendo el mismo), se acepta y se renueva
        /// -- el TTL protege contra OTRO usuario pisando el trabajo, no castiga a este por
        /// tardarse.
        /// </summary>
        public bool IsLockHeldBy(string userId, Guid lockToken) =>
            LockToken.HasValue && LockToken.Value == lockToken && LockedByUserId == userId;

        /// <summary>Nueva sesión de edición (token nuevo) para el usuario.</summary>
        public void AcquireLock(string userId, DateTime now, TimeSpan ttl)
        {
            LockToken = Guid.NewGuid();
            LockedByUserId = userId;
            LockedAt = now;
            LockExpiresAt = now.Add(ttl);
        }

        public void RenewLock(DateTime now, TimeSpan ttl) => LockExpiresAt = now.Add(ttl);

        public void ReleaseLock()
        {
            LockToken = null;
            LockedByUserId = null;
            LockedAt = null;
            LockExpiresAt = null;
        }
    }
}
