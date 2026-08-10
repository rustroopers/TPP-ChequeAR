using System;

namespace ChequeAR.Application.Exceptions
{
    /// <summary>
    /// La consulta al servidor terminológico (SNOMED CT) no se pudo completar.
    /// Lleva el status HTTP sugerido para que la API lo propague tal cual: 429 cuando
    /// el server nos limita por volumen, 503 cuando no hay server configurado o está
    /// caído. Así el front puede reaccionar distinto (p. ej. hacer backoff ante un 429).
    /// </summary>
    public class TerminologyException : Exception
    {
        public TerminologyException(int statusCode, string message, int? retryAfterSeconds = null)
            : base(message)
        {
            StatusCode = statusCode;
            RetryAfterSeconds = retryAfterSeconds;
        }

        /// Status HTTP que la API debería devolver al cliente.
        public int StatusCode { get; }

        /// Segundos sugeridos para reintentar (del header Retry-After), si el server lo mandó.
        public int? RetryAfterSeconds { get; }
    }
}
