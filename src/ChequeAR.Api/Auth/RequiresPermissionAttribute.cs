using System.Security.Claims;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using ChequeAR.Application.Security;

namespace ChequeAR.Api.Auth
{
    /// <summary>
    /// Filtro de borde para MVC. Reemplaza a [Authorize(Roles = "Administrador,Jefe")]:
    /// consulta RolePolicy -la MISMA matriz que ya aplican los servicios de Application vía
    /// IAccessGuard- en vez de repetir la lista de roles en cada controller. Cambiar quién
    /// puede hacer qué es cambiar RolePolicy, un solo archivo.
    ///
    /// No autenticado -&gt; comportamiento estándar de AuthorizeAttribute (redirect a
    /// /Account/Login). Autenticado pero sin el permiso -&gt; 403 (Error/Index), no el mismo
    /// redirect a login: el problema no es la sesión, reintentarla no cambia nada.
    /// </summary>
    public class RequiresPermissionAttribute : AuthorizeAttribute
    {
        private readonly string _permission;

        public RequiresPermissionAttribute(string permission)
        {
            _permission = permission;
        }

        protected override bool AuthorizeCore(HttpContextBase httpContext)
        {
            var principal = httpContext.User as ClaimsPrincipal;
            if (principal == null || !principal.Identity.IsAuthenticated) return false;
            return RolePolicy.Allows(new[] { CurrentUser.PrincipalRole(principal) }, _permission);
        }

        protected override void HandleUnauthorizedRequest(AuthorizationContext filterContext)
        {
            var principal = filterContext.HttpContext.User as ClaimsPrincipal;
            if (principal != null && principal.Identity.IsAuthenticated)
            {
                App_Start.ServiceComposition.AuditForbidden(CurrentUser.Id(principal),
                    Helpers.LogSanitizer.Path(filterContext.HttpContext.Request.Path));
                filterContext.Result = new RedirectToRouteResult(new RouteValueDictionary(
                    new { controller = "Error", action = "Index", code = 403 }));
                return;
            }
            base.HandleUnauthorizedRequest(filterContext);
        }
    }
}
