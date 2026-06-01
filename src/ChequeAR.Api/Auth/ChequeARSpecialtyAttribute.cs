using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Api.Auth
{
    /// <summary>Filtra resultados al subconjunto de especialidades del usuario.
    /// Administrador y Jefe bypassean este filtro.</summary>
    public class ChequeARSpecialtyAttribute : ActionFilterAttribute
    {
        public string RequiredSpecialtyQueryParam { get; set; } = "specialty";

        public override void OnActionExecuting(HttpActionContext actionContext)
        {
            var principal = actionContext.RequestContext.Principal as System.Security.Claims.ClaimsPrincipal;
            if (principal == null || !principal.Identity.IsAuthenticated)
            {
                actionContext.Response = actionContext.Request.CreateResponse(HttpStatusCode.Unauthorized);
                return;
            }

            if (principal.IsInRole(Role.Administrador) || principal.IsInRole(Role.Jefe)) return;

            var requested = actionContext.Request.GetQueryNameValuePairs()
                .FirstOrDefault(p => p.Key == RequiredSpecialtyQueryParam).Value;
            if (string.IsNullOrEmpty(requested)) return;

            var allowed = CurrentUser.Specialties(principal);
            if (!allowed.Contains(requested))
            {
                actionContext.Response = actionContext.Request.CreateResponse(HttpStatusCode.Forbidden);
            }
        }
    }
}
