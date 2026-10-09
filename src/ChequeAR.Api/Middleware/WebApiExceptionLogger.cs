using System.Web.Http.ExceptionHandling;
using Serilog;

namespace ChequeAR.Api.Middleware
{
    /// Loguea las excepciones que revientan dentro del pipeline de Web API.
    ///
    /// Sin esto son invisibles: Web API atrapa la excepcion, responde 500 y no la deja
    /// propagar, asi que Application_Error nunca corre y no se escribe ni en el event log
    /// de ASP.NET ni en Serilog. El unico sintoma era un 500 sin cuerpo util, que es lo
    /// mismo que pasaba en las vistas MVC antes de sacar HandleErrorAttribute.
    ///
    /// Es un logger, no un handler: no cambia la respuesta que ve el cliente (sigue siendo
    /// el 500 generico, sin filtrar stack traces a la red), solo deja el rastro en disco.
    public class WebApiExceptionLogger : ExceptionLogger
    {
        public override void Log(ExceptionLoggerContext context)
        {
            var request = context?.Request;
            var ex = context?.Exception;

            // Los loggers corren antes que los filtros, así que también llegan acá las reglas
            // de negocio que ApiExceptionFilter convierte en 4xx: quedan registradas, pero no
            // como error del sistema.
            if (ChequeAR.Api.Filters.ApiExceptionFilter.IsClientError(ex))
            {
                Serilog.Log.Information("API {Method} {Path} rechazada: {Reason}",
                    request?.Method?.Method, request?.RequestUri?.PathAndQuery, ex.Message);
                return;
            }

            Serilog.Log.Error(ex,
                "Excepcion no controlada en API {Method} {Path}",
                request?.Method?.Method,
                request?.RequestUri?.PathAndQuery);
        }
    }
}
