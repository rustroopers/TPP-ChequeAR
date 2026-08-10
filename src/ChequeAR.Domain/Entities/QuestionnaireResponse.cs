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
    }
}
