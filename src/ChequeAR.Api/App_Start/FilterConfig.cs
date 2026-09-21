using System.Web.Mvc;

namespace ChequeAR.Api.App_Start
{
    public class FilterConfig
    {
        public static void RegisterGlobalFilters(GlobalFilterCollection filters)
        {
            // Sin HandleErrorAttribute a proposito. Renderiza una vista "Error" que este
            // proyecto no tiene (la pagina de error es Views/Error/Index.cshtml, servida por
            // ErrorController via customErrors defaultRedirect="~/Error"), asi que fallaba con
            // "The view 'Error' or its master was not found" y tapaba la excepcion original.
            // Ademas marcaba la excepcion como manejada, con lo cual Application_Error no
            // corria y Serilog no llegaba a loguear nada.

            // Este SI se registra: PermissionForbiddenException (IAccessGuard.EnsureCan) no
            // es un error de infraestructura, es una decision de negocio con una pantalla
            // propia (403), y marcarla como manejada es exactamente lo correcto.
            filters.Add(new ChequeAR.Api.Filters.MvcPermissionExceptionFilter());
        }
    }
}
