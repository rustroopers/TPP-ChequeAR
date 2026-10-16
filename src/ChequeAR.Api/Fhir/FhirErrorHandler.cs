using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ChequeAR.Application.Fhir;
using Hl7.Fhir.Model;

namespace ChequeAR.Api.Fhir
{
    /// <summary>
    /// En /fhir/ todo error es un OperationOutcome. Los que arma Web API por su cuenta (401 sin
    /// token, ruta inexistente, método no permitido, excepción no controlada) salían como
    /// HttpError; se reemplazan por uno genérico, sin detalles internos.
    /// </summary>
    public class FhirErrorHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // _format: sólo JSON. Otro formato no se sirve, en vez de mandar JSON igual.
            if (IsFhir(request) && !JsonFormat(request))
                return request.CreateResponse(HttpStatusCode.NotAcceptable, OutcomeFor(HttpStatusCode.NotAcceptable));

            var response = await base.SendAsync(request, cancellationToken);
            if ((int)response.StatusCode < 400 || !IsFhir(request) || IsOperationOutcome(response)) return response;

            var outcome = OutcomeFor(response.StatusCode);
            var replacement = request.CreateResponse(response.StatusCode, outcome);
            foreach (var header in response.Headers) replacement.Headers.TryAddWithoutValidation(header.Key, header.Value);
            return replacement;
        }

        private static bool IsFhir(HttpRequestMessage request) =>
            request.RequestUri.AbsolutePath.StartsWith("/fhir/", StringComparison.OrdinalIgnoreCase);

        private static bool JsonFormat(HttpRequestMessage request)
        {
            foreach (var pair in request.GetQueryNameValuePairs())
            {
                if (pair.Key != "_format") continue;
                var v = (pair.Value ?? string.Empty).Trim().ToLowerInvariant();
                return v == "json" || v == "application/json" || v == "application/fhir+json";
            }
            return true;
        }

        private static bool IsOperationOutcome(HttpResponseMessage response) =>
            response.Content is ObjectContent content && content.Value is OperationOutcome;

        private static OperationOutcome OutcomeFor(HttpStatusCode status)
        {
            switch ((int)status)
            {
                case 401: return Single(OperationOutcome.IssueType.Login, "Falta iniciar sesión o el token venció.");
                case 403: return OperationOutcomes.Forbidden();
                case 404: return OperationOutcomes.NotFound();
                case 405: return Single(OperationOutcome.IssueType.NotSupported, "La operación no está disponible para este recurso.");
                case 406:
                case 415: return Single(OperationOutcome.IssueType.NotSupported, "Formato no soportado: usar application/fhir+json.");
                case 409: return Single(OperationOutcome.IssueType.Conflict, "El recurso cambió o está en uso.");
                case 428: return OperationOutcomes.PreconditionRequired();
                default:
                    return (int)status >= 500
                        ? Single(OperationOutcome.IssueType.Exception, "Error interno del servidor.")
                        : OperationOutcomes.Invalid();
            }
        }

        private static OperationOutcome Single(OperationOutcome.IssueType code, string diagnostics) => new OperationOutcome
        {
            Issue =
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
