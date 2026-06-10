using System.Web.Http;
using System.Web.Mvc;
using System.Web.Routing;
using ChequeAR.Api.App_Start;

namespace ChequeAR.Api
{
    public class MvcApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            ServiceComposition.Configure();
            System.Web.Mvc.DependencyResolver.SetResolver(ServiceComposition.MvcResolver);

            AreaRegistration.RegisterAllAreas();
            GlobalConfiguration.Configure(WebApiConfig.Register);
            GlobalConfiguration.Configuration.DependencyResolver = ServiceComposition.WebApiResolver;

            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
        }
    }
}
