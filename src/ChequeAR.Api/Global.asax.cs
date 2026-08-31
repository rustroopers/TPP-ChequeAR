using System.Web.Http;
using System.Web.Mvc;
using System.Web.Routing;
using ChequeAR.Api.App_Start;
using Serilog;

namespace ChequeAR.Api
{
    public class MvcApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            LoggingConfig.Configure();

            ServiceComposition.Configure();
            System.Web.Mvc.DependencyResolver.SetResolver(ServiceComposition.MvcResolver);

            AreaRegistration.RegisterAllAreas();
            GlobalConfiguration.Configure(WebApiConfig.Register);
            GlobalConfiguration.Configuration.DependencyResolver = ServiceComposition.WebApiResolver;

            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
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
            Log.CloseAndFlush();
        }
    }
}
