using System.Net;
using System.Net.Http;
using ChequeAR.Application.Exceptions;
using ChequeAR.Application.Fhir;

namespace ChequeAR.Api.Filters
{
    /// <summary>
    /// Traduce PermissionForbiddenException (lanzada por IAccessGuard.EnsureCan en los
    /// servicios de Application) a 403 en Web API. Global -no por acción, como el patrón
    /// existente de SpecialtyForbiddenException por controller- porque el permiso se decide
    /// una sola vez en el servicio, así que capturarlo una sola vez alcanza; no hay un
    /// significado distinto según el endpoint (a diferencia de SpecialtyForbiddenException,
    /// que a veces se traduce a 404 para no confirmar existencia de un recurso ajeno).
    /// </summary>
    public class PermissionExceptionFilter : System.Web.Http.Filters.ExceptionFilterAttribute
    {
        public override void OnException(System.Web.Http.Filters.HttpActionExecutedContext context)
        {
            if (context.Exception is PermissionForbiddenException)
            {
                context.Response = context.Request.CreateResponse(
                    HttpStatusCode.Forbidden, OperationOutcomes.Forbidden());
            }
        }
    }
}
