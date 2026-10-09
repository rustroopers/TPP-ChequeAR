using System.Configuration;
using System.Security.Claims;
using System.Web.Helpers;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Routing;
using System.Web.Security;
using ChequeAR.Api.App_Start;
using ChequeAR.Api.Auth;
using ChequeAR.Infrastructure.Oracle;
using Serilog;

namespace ChequeAR.Api
{
    public class MvcApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            LoggingConfig.Configure();

            // Antes de componer nada: ODP.NET tiene que saber donde esta el wallet antes
            // de la primera conexion, y ServiceComposition ya arma los repositorios.
            OracleWalletConfig.Configure(ConfigurationManager.AppSettings["Oracle:WalletDir"]);

            // La composición registra los controllers de Web API contra la misma configuración
            // que después arma WebApiConfig, y verifica todo el grafo antes de seguir.
            var container = ServiceComposition.Configure(GlobalConfiguration.Configuration);
            System.Web.Mvc.DependencyResolver.SetResolver(ServiceComposition.MvcResolver(container));

            AreaRegistration.RegisterAllAreas();
            GlobalConfiguration.Configure(WebApiConfig.Register);
            GlobalConfiguration.Configuration.DependencyResolver = ServiceComposition.WebApiResolver(container);

            // El anti-forgery, ante una identidad claims-based, exige NameIdentifier Y el claim
            // 'identityprovider' de ACS —el mensaje de error dice "or" pero pide los dos— y sin
            // ellos tira InvalidOperationException al renderizar @Html.AntiForgeryToken(), o sea
            // en toda vista con formulario. La sesion de ChequeAR no viene de un STS federado,
            // asi que en vez de fabricar un identityprovider falso se declara cual es el claim
            // que identifica al usuario.
            AntiForgeryConfig.UniqueClaimTypeIdentifier = ClaimTypes.NameIdentifier;

            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
        }

        /// El FormsAuthenticationModule arma un principal con el nombre y nada mas:
        /// UserData (donde viajan los roles) lo ignora. Sin esto User.IsInRole(...) da
        /// false para todo el mundo y el JWT de las islas saldria sin permisos.
        protected void Application_PostAuthenticateRequest()
        {
            var identity = Context.User?.Identity as FormsIdentity;
            if (identity == null || !identity.IsAuthenticated) return;

            var principal = SessionCookie.ToPrincipal(identity.Ticket);
            if (principal == null) return;

            Context.User = principal;
            System.Threading.Thread.CurrentPrincipal = principal;
        }

        /// Forms convierte cualquier 401 en un 302 al login. Para las islas eso es peor
        /// que el 401: reciben el HTML del login con status 200 y no pueden distinguir
        /// "sesion vencida" de una respuesta valida. En las rutas de API el 401 pasa tal cual.
        protected void Application_EndRequest()
        {
            if (Context.Response.StatusCode != 401) return;

            var path = Context.Request.AppRelativeCurrentExecutionFilePath ?? string.Empty;
            if (path.StartsWith("~/fhir/", System.StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("~/api/", System.StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("~/auth/", System.StringComparison.OrdinalIgnoreCase))
            {
                Context.Response.SuppressFormsAuthenticationRedirect = true;
            }
        }

        protected void Application_Error()
        {
            var ex = Server.GetLastError();
            if (ex == null) return;

            var request = Context?.Request;
            Log.Error(ex, "Excepcion no controlada en {Method} {Path}",
                request?.HttpMethod, request?.Path);
        }

        protected void Application_End()
        {
            // Antes de cerrar Serilog: los eventos que no se puedan persistir se loguean.
            ServiceComposition.Shutdown();
            Log.CloseAndFlush();
        }
    }
}
