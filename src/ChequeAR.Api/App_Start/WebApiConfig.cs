using System.Net.Http.Formatting;
using System.Web.Http;

namespace ChequeAR.Api.App_Start
{
    public static class WebApiConfig
    {
        public static void Register(HttpConfiguration config)
        {
            config.MapHttpAttributeRoutes();

            // Rutas FHIR R5
            config.Routes.MapHttpRoute(
                name: "FhirMetadata",
                routeTemplate: "fhir/r5/metadata",
                defaults: new { controller = "Metadata", action = "Get" });

            config.Routes.MapHttpRoute(
                name: "FhirResourceById",
                routeTemplate: "fhir/r5/{resourceType}/{id}",
                defaults: new { id = RouteParameter.Optional });

            // Resto en attribute routing

            // Soporte application/fhir+json (mapeo a JSON formatter)
            var jsonFormatter = config.Formatters.JsonFormatter;
            jsonFormatter.SupportedMediaTypes.Add(new System.Net.Http.Headers.MediaTypeHeaderValue("application/fhir+json"));
            jsonFormatter.SerializerSettings.NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore;
            config.Formatters.Remove(config.Formatters.XmlFormatter);
        }
    }
}
