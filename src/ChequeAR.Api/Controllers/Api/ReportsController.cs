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
        private readonly IQuestionnaireRepository _qRepo;

        public ReportsController(ReportExecutor executor, AuditDispatcher audit, IQuestionnaireRepository qRepo)
        {
            _executor = executor;
            _audit = audit;
            _qRepo = qRepo;
        }

        // Columnas (linkId -> texto de la pregunta) en orden, para pivotear el reporte.
        private async Task<List<KeyValuePair<string, string>>> QuestionColumnsAsync(System.Guid questionnaireId)
        {
            var cols = new List<KeyValuePair<string, string>>();
            var q = await _qRepo.GetAsync(questionnaireId);
            if (q?.Questions != null)
                foreach (var qq in q.Questions.OrderBy(x => x.OrderIndex))
                    cols.Add(new KeyValuePair<string, string>(qq.LinkId, qq.Text));
            return cols;
        }

        [HttpPost, Route("query"), Authorize]
        public async Task<IHttpActionResult> Query([FromBody] ReportQuery query)
        {
            if (query == null) return BadRequest("body required");
            var result = await _executor.ExecuteAsync(query);
            var wide = ReportPivot.ToWide(result, await QuestionColumnsAsync(query.QuestionnaireId));
            return Ok(new { total = wide.Total, rows = wide.Rows });
        }

        [HttpPost, Route("export"), Authorize]
        public async Task<HttpResponseMessage> Export([FromBody] ReportQuery query)
        {
            if (query == null) return Request.CreateResponse(HttpStatusCode.BadRequest);
            var longResult = await _executor.ExecuteAsync(query);
            var result = ReportPivot.ToWide(longResult, await QuestionColumnsAsync(query.QuestionnaireId));

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
