using System;
using System.Threading.Tasks;
using System.Web.Http;
using ChequeAR.Application.Services;

namespace ChequeAR.Api.Controllers.Api
{
    /// <summary>
    /// Evolución longitudinal de parámetros de un paciente (spec 010).
    ///
    /// No es un recurso FHIR: son proyecciones a medida para la pantalla Trend, así que va bajo
    /// `api/patients` (mismo estilo que `api/reports`) y no bajo `fhir/r5`. Se mantiene separado
    /// del <see cref="PatientController"/> —que expone Patient como recurso del estándar, en
    /// solo lectura— justamente para no mezclar la superficie FHIR con endpoints propios.
    /// </summary>
    [RoutePrefix("api/patients")]
    public class PatientTrendController : ApiController
    {
        private readonly PatientTrendService _svc;
        private readonly AccessAuditService _audit;

        public PatientTrendController(PatientTrendService svc, AccessAuditService audit)
        {
            _svc = svc;
            _audit = audit;
        }

        /// <summary>Parámetros graficables de un cuestionario (para poblar el selector).</summary>
        [HttpGet, Route("{hospitalId}/questionnaires/{questionnaireId:guid}/trend-parameters"), Authorize]
        public async Task<IHttpActionResult> TrendParameters(string hospitalId, Guid questionnaireId)
        {
            var result = await _svc.GetSelectableParametersAsync(questionnaireId);
            if (result == null) return NotFound();   // el cuestionario no existe
            return Ok(result);
        }

        /// <summary>Series de evolución de los parámetros pedidos, solo con fichas finalizadas.</summary>
        [HttpGet, Route("{hospitalId}/trend"), Authorize]
        public async Task<IHttpActionResult> Trend(
            string hospitalId, [FromUri] Guid? questionnaireId = null, [FromUri(Name = "linkId")] string[] linkId = null)
        {
            if (questionnaireId == null || questionnaireId.Value == Guid.Empty)
                return BadRequest("Falta el parámetro 'questionnaireId'.");
            if (linkId == null || linkId.Length == 0)
                return BadRequest("Hay que indicar al menos un 'linkId'.");

            var series = await _svc.GetTrendAsync(hospitalId, questionnaireId.Value, linkId);
            // Sólo si hubo algo que mostrar: un pedido sin parámetros válidos no leyó datos.
            if (series.Count > 0)
                _audit.TrendViewed(hospitalId, questionnaireId.Value, series.ConvertAll(s => s.LinkId));
            return Ok(new { QuestionnaireId = questionnaireId.Value, PatientHospitalId = hospitalId, Series = series });
        }
    }
}
