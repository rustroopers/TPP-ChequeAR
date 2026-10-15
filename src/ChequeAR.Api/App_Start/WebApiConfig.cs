using System.Web.Http;
using System.Web.Http.ExceptionHandling;
using ChequeAR.Api.Fhir;
using ChequeAR.Api.Middleware;

namespace ChequeAR.Api.App_Start
{
    public static class WebApiConfig
    {
        public static void Register(HttpConfiguration config)
        {
            // Sin esto, cualquier excepcion del pipeline de Web API se convierte en un 500
            // mudo: no propaga a Application_Error, asi que no queda rastro en ningun lado.
            config.Services.Add(typeof(IExceptionLogger), new WebApiExceptionLogger());

            // Sólo Bearer: aceptar la cookie de sesión dejaría la API expuesta a CSRF.
            config.SuppressDefaultHostAuthentication();
            config.Filters.Add(new HostAuthenticationFilter("Bearer"));

            // Excepciones de Application -> respuesta HTTP, en un solo lugar (ver
            // Filters/ApiExceptionFilter). Las acciones sólo capturan lo que para ellas
            // significa otra cosa.
            config.Filters.Add(new ChequeAR.Api.Filters.ApiExceptionFilter());

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
            // Ningún endpoint recibe formularios.
            foreach (var f in System.Linq.Enumerable.ToList(System.Linq.Enumerable.OfType<System.Net.Http.Formatting.FormUrlEncodedMediaTypeFormatter>(config.Formatters)))
                config.Formatters.Remove(f);
        }
    }
}
