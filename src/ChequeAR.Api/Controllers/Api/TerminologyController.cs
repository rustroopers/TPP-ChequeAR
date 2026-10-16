using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using ChequeAR.Api.Auth;
using ChequeAR.Application.Exceptions;
using ChequeAR.Application.Fhir;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Api.Controllers.Api
{
    /// Autocompletado de códigos SNOMED CT para el editor de cuestionarios.
    /// El front manda el texto de la pregunta como filter y recibe un ValueSet con la
    /// expansión. La llamada al servidor terminológico se hace server-side
    /// (ver TerminologyClient): así no exponemos el server externo al navegador
    /// (CORS/licencia) y podemos cambiar de instancia sólo por config.
    [RoutePrefix("fhir/r5/ValueSet")]
    public class TerminologyController : ApiController
    {
        private readonly TerminologyService _terminology;

        public TerminologyController(TerminologyService terminology)
        {
            _terminology = terminology;
        }

        // Los 3 roles: además de quien edita cuestionarios (para codificar preguntas), el
        // renderer de fichas (questionnaire-renderer) también lo usa para que Medico pueda
        // responder preguntas codificadas -- negárselo le rompería la carga de fichas.
        [HttpGet, Route("$expand"), RequiresPermissionApi(Permissions.TerminologyExpand)]
        public async Task<IHttpActionResult> Expand([FromUri] string filter = null, [FromUri] int count = 15)
        {
            // Menos de 3 caracteres da ruido y castiga al server terminológico: el servicio
            // devuelve la expansión vacía sin consultarlo.
            try
            {
                return Ok(await _terminology.ExpandAsync(filter, count, DateTime.UtcNow));
            }
            catch (TerminologyException ex)
            {
                // El client ya decidió el status (429 rate limit, 503 sin configurar, 502 otro).
                // El tiempo de espera va DENTRO del diagnostics: OperationOutcome no tiene un
                // campo para eso, y perderlo dejaría al front reintentando a ciegas.
                return Content((HttpStatusCode)ex.StatusCode,
                    OperationOutcomes.Transient(ex.Message, ex.RetryAfterSeconds));
            }
            catch (Exception ex)
            {
                // Timeout / server caído / respuesta ilegible: el front lo muestra amable. El
                // detalle queda en el log, no en la respuesta.
                Serilog.Log.Warning(ex, "Falló la consulta al servidor terminológico (filtro {Filter})", filter);
                return Content((HttpStatusCode)502,
                    OperationOutcomes.Transient("No se pudo consultar SNOMED CT."));
            }
        }
    }
}
