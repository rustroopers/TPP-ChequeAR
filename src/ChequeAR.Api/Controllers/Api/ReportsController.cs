using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using ChequeAR.Api.Auth;
using ChequeAR.Application.Dtos;
using ChequeAR.Application.Exporters;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Reports;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Api.Controllers.Api
{
    [RoutePrefix("api/reports")]
    public class ReportsController : ApiController
    {
        private readonly ReportExecutor _executor;
        private readonly AuditDispatcher _audit;

        public ReportsController(ReportExecutor executor, AuditDispatcher audit)
        {
            _executor = executor;
            _audit = audit;
        }

        [HttpPost, Route("query"), Authorize]
        public async Task<IHttpActionResult> Query([FromBody] ReportQuery query)
        {
            if (query == null) return BadRequest("body required");
            try
            {
                // El executor ya devuelve el formato ancho: pivotear era responsabilidad del
                // llamador y la pantalla de Preview (MVC) se olvidaba de hacerlo, con lo cual
                // mostraba el formato largo crudo. Ahora no hay forma de equivocarse.
                var wide = await _executor.ExecuteAsync(query);
                return Ok(new { total = wide.Total, rows = wide.Rows, page = query.Page, pageSize = query.PageSize });
            }
            catch (ArgumentException ex)
            {
                // Filtro mal armado (linkId ajeno al cuestionario, operador invalido, valor no
                // parseable): es culpa del pedido, no del servidor.
                return BadRequest(ex.Message);
            }
        }

        /// <summary>
        /// Estadísticas descriptivas de la cohorte filtrada (FR-008). Recibe la MISMA query
        /// que /query: si recibiera sólo un id de cuestionario, los números no
        /// corresponderían a los filtros que la persona tiene puestos en pantalla.
        /// </summary>
        [HttpPost, Route("stats"), Authorize]
        public async Task<IHttpActionResult> Stats([FromBody] ReportQuery query)
        {
            if (query == null) return BadRequest("body required");
            try
            {
                return Ok(await _executor.ExecuteStatsAsync(query));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                return BadRequest(ex.Message);
            }
        }

        /// <summary>
        /// Exporta las estadísticas descriptivas (FR-008) a Excel, opcionalmente acotadas a
        /// las preguntas que la persona haya seleccionado en pantalla. Solo Excel: un export a
        /// Word sin los gráficos nativos (torta/barra) sería una versión pobre de este mismo
        /// archivo, así que no se ofrece.
        /// </summary>
        [HttpPost, Route("stats/export"), Authorize]
        public async Task<HttpResponseMessage> StatsExport([FromBody] StatsExportRequest request)
        {
            if (request == null || request.Query == null) return Request.CreateResponse(HttpStatusCode.BadRequest);

            ReportStats stats;
            try
            {
                stats = await _executor.ExecuteStatsAsync(request.Query);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                return Request.CreateErrorResponse(HttpStatusCode.BadRequest, ex.Message);
            }

            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            _audit.Enqueue(AuditEventBuilder.For(
                "execute",
                "Report/statsExport?questionnaire=" + request.Query.QuestionnaireId + "&questions=" + stats.Questions.Count,
                userId, category: "export"));

            var stream = new StatsXlsxExporter().Export(stats, request.LinkIds, request.ChartTypes);

            var resp = Request.CreateResponse(HttpStatusCode.OK);
            resp.Content = new StreamContent(stream);
            resp.Content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            resp.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = "estadisticas.xlsx" };
            return resp;
        }

        [HttpPost, Route("export"), Authorize]
        public async Task<HttpResponseMessage> Export([FromBody] ReportQuery query)
        {
            if (query == null) return Request.CreateResponse(HttpStatusCode.BadRequest);

            ReportResult result;
            try
            {
                // Sin paginar: una exportacion que saliera cortada en la primera pagina seria
                // peor que un error, porque nadie lo notaria.
                result = await _executor.ExecuteAllAsync(query);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                return Request.CreateErrorResponse(HttpStatusCode.BadRequest, ex.Message);
            }

            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            // FR-009 pide registrar QUE consulta y CUANTOS registros, no solo que hubo un export.
            _audit.Enqueue(AuditEventBuilder.For(
                "execute",
                "Report/export?questionnaire=" + query.QuestionnaireId + "&rows=" + result.Total,
                userId, category: "export"));

            var format = (query.Format ?? "xlsx").ToLowerInvariant();
            var stream = format == "csv" ? new CsvExporter().Export(result) : new XlsxExporter().Export(result);

            var resp = Request.CreateResponse(HttpStatusCode.OK);
            resp.Content = new StreamContent(stream);
            if (format == "csv")
            {
                resp.Content.Headers.ContentType = new MediaTypeHeaderValue("text/csv") { CharSet = "utf-8" };
                resp.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = "reporte.csv" };
            }
            else
            {
                resp.Content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
                resp.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = "reporte.xlsx" };
            }
            return resp;
        }
    }
}
