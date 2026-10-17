using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;

namespace ChequeAR.Application.Fhir
{
    /// <summary>
    /// Fábricas de OperationOutcome, el recurso que FHIR define para reportar errores.
    ///
    /// Reemplazan al <c>{ "error": "..." }</c> que devolvían los controllers: ese formato es
    /// propio y ninguna herramienta del ecosistema sabe leerlo. Una API que responde bien en
    /// el camino feliz pero inventa su propio formato de error no es interoperable — y es lo
    /// primero que se rompe en una integración real.
    ///
    /// El catálogo de códigos está en specs/009-conformidad-fhir-capas/contracts/fhir-api.md.
    /// Los diagnostics van en español: los muestran las pantallas del sistema.
    /// </summary>
    public static class OperationOutcomes
    {
        public static OperationOutcome NotFound(string diagnostics = null)
            => Single(OperationOutcome.IssueType.NotFound,
                      diagnostics ?? "El recurso solicitado no existe.");

        public static OperationOutcome Forbidden(string diagnostics = null)
            => Single(OperationOutcome.IssueType.Forbidden,
                      diagnostics ?? "No tiene permiso para realizar esta operación.");

        public static OperationOutcome Invalid(string diagnostics = null)
            => Single(OperationOutcome.IssueType.Invalid,
                      diagnostics ?? "El contenido enviado no se pudo interpretar.");

        /// <summary>422: la petición se entiende, pero viola una regla del negocio.</summary>
        public static OperationOutcome Unprocessable(string diagnostics)
            => Single(OperationOutcome.IssueType.BusinessRule, diagnostics);

        /// <summary>
        /// 428 Precondition Required (RFC 6585). Sin If-Match no hay forma de saber si el
        /// cliente vio la última versión, y una edición concurrente pisaría trabajo ajeno
        /// en silencio.
        /// </summary>
        public const string LockHolderUrl = "https://chequear.garrahan/StructureDefinition/lock-holder";
        public const string LockExpiresUrl = "https://chequear.garrahan/StructureDefinition/lock-expires";

        /// <summary>
        /// 409 por la sesión de edición de otro usuario. Quién la tiene y hasta cuándo no tienen
        /// lugar en el OperationOutcome: van como extensiones, que el renderer lee para avisar.
        /// </summary>
        public static OperationOutcome LockConflict(string diagnostics, string lockedBy, System.DateTime? expiresAt)
        {
            var outcome = Single(OperationOutcome.IssueType.LockError, diagnostics);
            if (!string.IsNullOrEmpty(lockedBy)) outcome.Extension.Add(new Extension(LockHolderUrl, new FhirString(lockedBy)));
            if (expiresAt.HasValue)
                outcome.Extension.Add(new Extension(LockExpiresUrl, new Instant(new System.DateTimeOffset(
                    System.DateTime.SpecifyKind(expiresAt.Value, System.DateTimeKind.Utc)))));
            return outcome;
        }

        public static OperationOutcome PreconditionRequired(string diagnostics = null)
            => Single(OperationOutcome.IssueType.Required,
                      diagnostics ?? "Falta el header If-Match con la versión del recurso (ej: If-Match: W/\"3\").");

        /// <summary>
        /// 409: alguien más guardó en el medio. Las dos versiones van en el diagnostics
        /// porque es lo que la persona necesita para entender qué pasó.
        /// </summary>
        public static OperationOutcome Conflict(int expectedVersion, int actualVersion)
            => Single(OperationOutcome.IssueType.Conflict,
                      "Otra persona modificó este recurso mientras usted lo editaba. " +
                      "Su versión era la " + expectedVersion + " y la actual es la " + actualVersion + ".");

        /// <summary>
        /// Fallo temporal de un servicio externo (terminología, hospital). Se distingue de un
        /// error del cliente: reintentar puede funcionar.
        /// </summary>
        public static OperationOutcome Transient(string diagnostics, int? retryAfterSeconds = null)
        {
            var text = diagnostics;
            if (retryAfterSeconds.HasValue && retryAfterSeconds.Value > 0)
                text += " Reintente en " + retryAfterSeconds.Value + " segundos.";
            return Single(OperationOutcome.IssueType.Transient, text);
        }

        /// <summary>
        /// Para el caso de la baja: si el cuestionario tiene fichas cargadas se archiva en vez
        /// de eliminarse. Un 204 sería mentira —el recurso sigue existiendo— así que se
        /// responde 200 explicando qué pasó realmente.
        /// </summary>
        public static OperationOutcome Informational(string diagnostics)
            => new OperationOutcome
            {
                Issue = new List<OperationOutcome.IssueComponent>
                {
                    new OperationOutcome.IssueComponent
                    {
                        Severity = OperationOutcome.IssueSeverity.Information,
                        Code = OperationOutcome.IssueType.Informational,
                        Diagnostics = diagnostics
                    }
                }
            };

        /// <summary>
        /// UN issue por cada regla incumplida, no un único mensaje concatenado. Es la
        /// diferencia entre "arreglá esto" y "arreglá algo de esta lista".
        /// </summary>
        public static OperationOutcome ForValidationErrors(IEnumerable<string> messages)
        {
            var list = (messages ?? Enumerable.Empty<string>())
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .ToList();

            if (list.Count == 0) return Unprocessable("La validación falló sin detalle disponible.");

            return new OperationOutcome
            {
                Issue = list.Select(m => new OperationOutcome.IssueComponent
                {
                    Severity = OperationOutcome.IssueSeverity.Error,
                    Code = OperationOutcome.IssueType.Invariant,
                    Diagnostics = m
                }).ToList()
            };
        }

        private static OperationOutcome Single(OperationOutcome.IssueType code, string diagnostics)
        {
            return new OperationOutcome
            {
                Issue = new List<OperationOutcome.IssueComponent>
                {
                    new OperationOutcome.IssueComponent
                    {
                        Severity = OperationOutcome.IssueSeverity.Error,
                        Code = code,
                        Diagnostics = diagnostics
                    }
                }
            };
        }
    }
}
