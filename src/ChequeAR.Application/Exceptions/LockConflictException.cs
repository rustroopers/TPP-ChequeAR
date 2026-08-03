using System;

namespace ChequeAR.Application.Exceptions
{
    /// <summary>
    /// Alguien más tiene tomado el lock de edición de este QuestionnaireResponse (otra
    /// llamada/sesión en curso) y todavía no venció. Se traduce a 409 Conflict en la API.
    /// </summary>
    public class LockConflictException : Exception
    {
        public LockConflictException(string lockedByUserId, DateTime? lockExpiresAt)
            : base("El cuestionario lo está completando otro usuario (" + lockedByUserId + ").")
        {
            LockedByUserId = lockedByUserId;
            LockExpiresAt = lockExpiresAt;
        }

        public string LockedByUserId { get; }
        public DateTime? LockExpiresAt { get; }
    }
}
