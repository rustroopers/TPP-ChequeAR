using System;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using ChequeAR.Api.Auth;
using ChequeAR.Application.Fhir;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Api.Controllers.Api
{
    /// CRUD de consultas guardadas (E1 listado, E6 modal guardar).
    /// Spec: specs/004-analisis-exportacion/api.md §`/api/reports/saved`.
    [RoutePrefix("api/reports/saved")]
    public class SavedReportController : ApiController
    {
        private readonly SavedReportService _saved;
        private readonly QuestionnaireService _questionnaires;
        public SavedReportController(SavedReportService saved, QuestionnaireService questionnaires)
        {
            _saved = saved;
            _questionnaires = questionnaires;
        }

        [HttpGet, Route(""), Authorize]
        public async Task<IHttpActionResult> List()
        {
            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            var specialties = CurrentUser.Specialties(User as ClaimsPrincipal);
            var items = await _saved.ListForUserAsync(userId, specialties);
            return Ok(new { total = items.Count, results = items });
        }

        [HttpGet, Route("{id:guid}"), Authorize]
        public async Task<IHttpActionResult> Get(Guid id)
        {
            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            var specialties = CurrentUser.Specialties(User as ClaimsPrincipal);
            var resultado = await _saved.GetForUserAsync(id, userId, specialties);
            return Responder(resultado, r => Ok(r));
        }

        [HttpPost, Route(""), Authorize]
        public async Task<IHttpActionResult> Create([FromBody] SavedReport body)
        {
            if (body == null) return BadRequest("body required");
            var creada = await _saved.CreateAsync(body, CurrentUser.Id(User as ClaimsPrincipal));
            return Content(HttpStatusCode.Created, creada);
        }

        [HttpPut, Route("{id:guid}"), Authorize]
        public async Task<IHttpActionResult> Update(Guid id, [FromBody] SavedReport body)
        {
            if (body == null) return BadRequest("body required");

            var resultado = await _saved.UpdateAsync(id, body, CurrentUser.Id(User as ClaimsPrincipal));
            return Responder(resultado, r => Ok(r));
        }

        [HttpDelete, Route("{id:guid}"), Authorize]
        public async Task<IHttpActionResult> Delete(Guid id)
        {
            var resultado = await _saved.DeleteAsync(id, CurrentUser.Id(User as ClaimsPrincipal));
            return Responder(resultado, _ => StatusCode(HttpStatusCode.NoContent));
        }

        /// <summary>
        /// Traduce el resultado del servicio a una respuesta HTTP. La REGLA (quién puede
        /// modificar qué) vive en el servicio; acá sólo queda elegir el código de estado,
        /// que es lo único propio de la capa web.
        /// </summary>
        private IHttpActionResult Responder(
            SavedReportResult resultado, Func<SavedReport, IHttpActionResult> ok)
        {
            switch (resultado.Outcome)
            {
                case SavedReportOutcome.NotFound:
                    return Content(HttpStatusCode.NotFound, OperationOutcomes.NotFound());
                case SavedReportOutcome.Forbidden:
                    return Content(HttpStatusCode.Forbidden, OperationOutcomes.Forbidden(
                        "Sólo quien creó la consulta guardada puede modificarla o eliminarla."));
                default:
                    return ok(resultado.Report);
            }
        }

        // GET /api/reports/preview-columns?questionnaireId={guid}
        // Devuelve linkIds + types disponibles para el query builder (E2).
        [HttpGet, Route("~/api/reports/preview-columns"), Authorize]
        public async Task<IHttpActionResult> PreviewColumns([FromUri] Guid questionnaireId)
        {
            var q = await _questionnaires.GetAsync(questionnaireId);
            if (q == null) return NotFound();
            var cols = new System.Collections.Generic.List<object>();
            if (q.Questions != null)
            {
                foreach (var qq in q.Questions)
                {
                    cols.Add(new { linkId = qq.LinkId, text = qq.Text, type = qq.Type.ToString().ToLowerInvariant() });
                }
            }
            return Ok(new { questionnaireId = questionnaireId, columns = cols });
        }
    }
}
