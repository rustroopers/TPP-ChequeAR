using System.Web.Mvc;
using System.Web.Routing;

namespace ChequeAR.Api.App_Start
{
    public class RouteConfig
    {
        public static void RegisterRoutes(RouteCollection routes)
        {
            routes.IgnoreRoute("{resource}.axd/{*pathInfo}");

            // Ruta cosmética: la pantalla de pacientes vive en EncountersController.Index,
            // pero la URL pública es /Pacientes — el usuario final no debería ver el nombre
            // técnico del recurso interno ("Encounters" ni el ex-verbo "Search"). Se
            // registra ANTES de la default para que Url.Action("Index","Encounters") también
            // resuelva a /Pacientes; las demás acciones del controller caen por la default
            // (/Encounters/Load, /Encounters/History, etc.).
            routes.MapRoute(
                name: "Pacientes",
                url: "Pacientes/{id}",
                defaults: new { controller = "Encounters", action = "Index", id = UrlParameter.Optional }
            );

            routes.MapRoute(
                name: "Default",
                url: "{controller}/{action}/{id}",
                defaults: new { controller = "Home", action = "Index", id = UrlParameter.Optional }
            );
        }
    }
}
