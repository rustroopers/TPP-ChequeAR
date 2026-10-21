using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Web.Http;
using System.Web.Http.Controllers;
using ChequeAR.Application.Fhir;
using ChequeAR.Application.Security;

namespace ChequeAR.Api.Auth
{
    /// <summary>
    /// Equivalente a RequiresPermissionAttribute (MVC) para Web API. Reemplaza a
    /// [Authorize(Roles = "Administrador,Jefe")]: consulta RolePolicy en vez de repetir la
    /// lista de roles en cada controller.
    ///
    /// No autenticado -&gt; 401 estándar. Autenticado pero sin el permiso -&gt; 403 con
    /// OperationOutcome, igual formato que SpecialtyForbiddenException en el resto de la API.
    /// </summary>
    public class RequiresPermissionApiAttribute : AuthorizeAttribute
    {
        private readonly string _permission;

        public RequiresPermissionApiAttribute(string permission)
        {
            _permission = permission;
        }

        protected override bool IsAuthorized(HttpActionContext actionContext)
        {
            var principal = actionContext.RequestContext?.Principal as ClaimsPrincipal;
            if (principal == null || !principal.Identity.IsAuthenticated) return false;
            return RolePolicy.Allows(new[] { CurrentUser.PrincipalRole(principal) }, _permission);
        }

        protected override void HandleUnauthorizedRequest(HttpActionContext actionContext)
        {
            var principal = actionContext.RequestContext?.Principal as ClaimsPrincipal;
            if (principal != null && principal.Identity.IsAuthenticated)
            {
                actionContext.Response = actionContext.Request.CreateResponse(
                    HttpStatusCode.Forbidden, OperationOutcomes.Forbidden());
                return;
            }
            base.HandleUnauthorizedRequest(actionContext);
        }
    }
}
