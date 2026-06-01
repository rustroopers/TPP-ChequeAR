using System.Configuration;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
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
            var result = await _executor.ExecuteAsync(query);
            return Ok(new { total = result.Total, rows = result.Rows });
        }

        [HttpPost, Route("export"), Authorize]
        public async Task<HttpResponseMessage> Export([FromBody] ReportQuery query)
        {
            if (query == null) return Request.CreateResponse(HttpStatusCode.BadRequest);
            var result = await _executor.ExecuteAsync(query);

            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            _audit.Enqueue(AuditEventBuilder.For("execute", "Report/export", userId, category: "export"));

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
