using System;

namespace ChequeAR.Application.Exceptions
{
    /// <summary>
    /// Alguien más modificó el recurso entre que este cliente lo leyó y lo quiso guardar.
    /// Se traduce a 409 Conflict en la API (FHIR, "Managing Resource Contention").
    /// </summary>
    public class ConcurrencyConflictException : Exception
    {
        public ConcurrencyConflictException(int expectedVersion, int actualVersion)
            : base("El cuestionario fue modificado por otra persona (esperaba la versión " +
                   expectedVersion + ", en la base está la " + actualVersion + ").")
        {
            ExpectedVersion = expectedVersion;
            ActualVersion = actualVersion;
        }

        public int ExpectedVersion { get; }
        public int ActualVersion { get; }
    }
}
