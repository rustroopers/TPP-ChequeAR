using System.Web.Http;
using ChequeAR.Api.Fhir;

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

            // El formatter de FHIR va PRIMERO: la negociación de contenido recorre la cadena
            // en orden, así que ser el primero es lo que hace que application/fhir+json sea el
            // comportamiento por omisión y no una opción que el consumidor deba pedir (FR-002).
            //
            // Sólo se queda con los tipos que derivan de Resource (ver CanWriteType), de modo
            // que los endpoints que no son FHIR —reportes, usuarios, adjuntos, auth— siguen
            // resolviéndose con el serializador JSON de siempre.
            config.Formatters.Insert(0, new FhirMediaTypeFormatter());

            var jsonFormatter = config.Formatters.JsonFormatter;
            jsonFormatter.SerializerSettings.NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore;
            config.Formatters.Remove(config.Formatters.XmlFormatter);
        }
    }
}
