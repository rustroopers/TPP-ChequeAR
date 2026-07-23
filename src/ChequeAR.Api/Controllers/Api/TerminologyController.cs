using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using ChequeAR.Application.Exceptions;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Enums;
using Newtonsoft.Json.Linq;

namespace ChequeAR.Api.Controllers.Api
{
    /// Autocompletado de códigos SNOMED CT para el editor de cuestionarios.
    /// El front manda el texto de la pregunta como filter y recibe una lista
    /// {code, display}. La llamada al servidor terminológico se hace server-side
    /// (ver TerminologyClient): así no exponemos el server externo al navegador
    /// (CORS/licencia) y podemos cambiar de instancia sólo por config.
    [RoutePrefix("fhir/r5/ValueSet")]
    public class TerminologyController : ApiController
    {
        private readonly ITerminologyClient _terminology;

        public TerminologyController(ITerminologyClient terminology)
        {
            _terminology = terminology;
        }

        // Sólo quienes editan cuestionarios necesitan codificar preguntas.
        [HttpGet, Route("$expand"), Authorize(Roles = Role.Administrador + "," + Role.Jefe)]
        public async Task<IHttpActionResult> Expand([FromUri] string filter = null, [FromUri] int count = 15)
        {
            // Menos de 3 caracteres da ruido y castiga al server terminológico: no buscamos.
            if (string.IsNullOrWhiteSpace(filter) || filter.Trim().Length < 3)
                return Ok(new { system = "http://snomed.info/sct", results = new object[0] });

            try
            {
                var json = await _terminology.ExpandSnomedAsync(filter.Trim(), count);
                var results = new List<object>();
                var contains = JObject.Parse(json)["expansion"]?["contains"] as JArray;
                foreach (var c in contains ?? new JArray())
                {
                    var code = (string)c["code"];
                    if (string.IsNullOrEmpty(code)) continue;
                    results.Add(new { code = code, display = (string)c["display"] });
                }
                return Ok(new { system = "http://snomed.info/sct", results = results });
            }
            catch (TerminologyException ex)
            {
                // El client ya decidió el status (429 rate limit, 503 sin configurar, 502 otro).
                // retryAfter viaja en el body para que el front sepa cuánto esperar.
                return Content((HttpStatusCode)ex.StatusCode,
                    new { error = ex.Message, retryAfter = ex.RetryAfterSeconds });
            }
            catch (Exception ex)
            {
                // Timeout / server caído / respuesta ilegible: el front lo muestra amable.
                return Content((HttpStatusCode)502, new { error = "No se pudo consultar SNOMED CT: " + ex.Message });
            }
        }
    }
}
