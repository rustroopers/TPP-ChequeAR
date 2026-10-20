using System.Web.Mvc;
using System.Web.Routing;
using ChequeAR.Application.Exceptions;

namespace ChequeAR.Api.Filters
{
    /// <summary>
    /// Equivalente MVC de PermissionExceptionFilter: si un servicio de Application lanza
    /// PermissionForbiddenException dentro de una acción MVC (p. ej. QuestionnairesController
    /// llamando a QuestionnaireService.PublishAsync), se traduce a la misma pantalla 403
    /// (Error/Index) que usa RequiresPermissionAttribute cuando el chequeo se hace ANTES de
    /// entrar a la acción. Dos caminos, un solo destino.
    /// </summary>
    public class MvcPermissionExceptionFilter : FilterAttribute, IExceptionFilter
    {
        public void OnException(ExceptionContext filterContext)
        {
            if (!(filterContext.Exception is PermissionForbiddenException)) return;

            filterContext.ExceptionHandled = true;
            ChequeAR.Api.App_Start.ServiceComposition.AuditForbidden(
                ChequeAR.Api.Auth.CurrentUser.Id(filterContext.HttpContext.User as System.Security.Claims.ClaimsPrincipal),
                ChequeAR.Api.Helpers.LogSanitizer.Path(filterContext.HttpContext.Request.Path));
            filterContext.Result = new RedirectToRouteResult(new RouteValueDictionary(
                new { controller = "Error", action = "Index", code = 403 }));
        }
    }
}
