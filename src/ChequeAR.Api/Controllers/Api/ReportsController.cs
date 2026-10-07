using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Http;
using ChequeAR.Api.Auth;
using ChequeAR.Application.Dtos;
using ChequeAR.Application.Exporters;
using ChequeAR.Application.Reports;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Api.Controllers.Api
{
    [RoutePrefix("api/reports")]
    public class ReportsController : ApiController
    {
        private readonly ReportExecutor _executor;
        private readonly AccessAuditService _audit;

        public ReportsController(ReportExecutor executor, AccessAuditService audit)
        {
            _executor = executor;
            _audit = audit;
        }

        [HttpPost, Route("query"), RequiresPermissionApi(Permissions.ReportRead)]
        public async Task<IHttpActionResult> Query([FromBody] ReportQuery query)
        {
            if (query == null) return BadRequest("body required");
            // Los errores del pedido (filtro mal armado, cuestionario inexistente, tope
            // superado) salen del executor como excepciones de Application y los traduce
            // ApiExceptionFilter; el executor ya devuelve el formato ancho.
            var wide = await _executor.ExecuteAsync(query);
            return Ok(new { total = wide.Total, rows = wide.Rows, page = query.Page, pageSize = query.PageSize });
        }

        /// <summary>
        /// Estadísticas descriptivas de la cohorte filtrada (FR-008). Recibe la MISMA query
        /// que /query: si recibiera sólo un id de cuestionario, los números no
        /// corresponderían a los filtros que la persona tiene puestos en pantalla.
        /// </summary>
        [HttpPost, Route("stats"), RequiresPermissionApi(Permissions.ReportRead)]
        public async Task<IHttpActionResult> Stats([FromBody] ReportQuery query)
        {
            if (query == null) return BadRequest("body required");
            return Ok(await _executor.ExecuteStatsAsync(query));
        }

        /// <summary>
        /// Exporta las estadísticas descriptivas (FR-008) a Excel, opcionalmente acotadas a
        /// las preguntas que la persona haya seleccionado en pantalla. Solo Excel: un export a
        /// Word sin los gráficos nativos (torta/barra) sería una versión pobre de este mismo
        /// archivo, así que no se ofrece.
        /// </summary>
        [HttpPost, Route("stats/export"), RequiresPermissionApi(Permissions.ReportRead)]
        public async Task<HttpResponseMessage> StatsExport([FromBody] StatsExportRequest request)
        {
            if (request == null || request.Query == null) return Request.CreateResponse(HttpStatusCode.BadRequest);

            var stats = await _executor.ExecuteStatsAsync(request.Query);

            _audit.StatsExported(request.Query, stats.Questions.Count);

            var stream = StatsXlsxExporter.Export(stats, request.LinkIds, request.ChartTypes);

            var resp = Request.CreateResponse(HttpStatusCode.OK);
            resp.Content = new StreamContent(stream);
            resp.Content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            resp.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = $"estadisticas_{DateTime.Now:yyyy-MM-dd}.xlsx" };
            return resp;
        }

        [HttpPost, Route("export"), RequiresPermissionApi(Permissions.ReportRead)]
        public async Task<HttpResponseMessage> Export([FromBody] ReportQuery query)
        {
            if (query == null) return Request.CreateResponse(HttpStatusCode.BadRequest);

            // Sin paginar: una exportacion que saliera cortada en la primera pagina seria
            // peor que un error, porque nadie lo notaria.
            var result = await _executor.ExecuteAllAsync(query);

            // FR-009 pide registrar QUE consulta y CUANTOS registros, no solo que hubo un export.
            _audit.ReportExported(query, result.Total);

            var format = (query.Format ?? "xlsx").ToLowerInvariant();
            var stream = format == "csv" ? CsvExporter.Export(result) : XlsxExporter.Export(result);

            var resp = Request.CreateResponse(HttpStatusCode.OK);
            resp.Content = new StreamContent(stream);
            if (format == "csv")
            {
                resp.Content.Headers.ContentType = new MediaTypeHeaderValue("text/csv") { CharSet = "utf-8" };
                resp.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = $"reporte_{DateTime.Now:yyyy-MM-dd}.csv" };
            }
            else
            {
                resp.Content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
                resp.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = $"reporte_{DateTime.Now:yyyy-MM-dd}.xlsx" };
            }
            return resp;
        }
    }
}
