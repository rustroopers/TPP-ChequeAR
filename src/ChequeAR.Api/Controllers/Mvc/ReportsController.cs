using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using ChequeAR.Api.Models;
using ChequeAR.Application.Dtos;
using ChequeAR.Application.Services;
using ChequeAR.Application.Reports;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace ChequeAR.Api.Controllers.Mvc
{
    [Authorize]
    public class ReportsController : Controller
    {
        private readonly SavedReportService _saved;
        private readonly ReportExecutor _executor;
        private readonly QuestionnaireService _questionnaires;

        public ReportsController(SavedReportService saved, ReportExecutor executor, QuestionnaireService questionnaires)
        {
            _saved = saved;
            _executor = executor;
            _questionnaires = questionnaires;
        }

        // E1
        public async Task<ActionResult> Index()
        {
            ViewBag.Title = "Reportes";
            ViewBag.PageId = "reports";
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Reportes", (string)null)
            };
            var model = new ReportListViewModel();
            try
            {
                // Misma identidad que estampa SessionToken al emitir el bearer: sin login es
                // "dev", con login el usuario real. Antes esta pantalla usaba "anonymous",
                // así que nunca listaba los reportes que la API guardaba bajo "dev" —el
                // listado salía siempre vacío aunque hubiera consultas guardadas.
                var userId = (User?.Identity != null && User.Identity.IsAuthenticated)
                    ? User.Identity.Name : "dev";
                var saved = await _saved.ListForUserAsync(userId, new string[0]);
                // El titulo del cuestionario base, no su GUID. Se cachea porque varios
                // reportes guardados suelen apuntar al mismo cuestionario.
                var titles = new Dictionary<Guid, string>();
                foreach (var r in saved)
                {
                    if (!titles.ContainsKey(r.QuestionnaireId))
                        titles[r.QuestionnaireId] = (await _questionnaires.GetAsync(r.QuestionnaireId))?.Title;

                    model.Items.Add(new SavedReportItem
                    {
                        Id = r.Id, Name = r.Name,
                        QuestionnaireTitle = titles[r.QuestionnaireId] ?? "(cuestionario eliminado)",
                        Author = r.CreatedBy,
                        LastUsedAt = ChequeAR.Api.Helpers.ArgentinaTime.FormatDate(r.UpdatedAt)
                    });
                }
            }
            catch (Exception)
            {
                model.Items.Clear();
                ViewBag.DataError = "Ocurrió un error al procesar la solicitud.";
            }
            return View(model);
        }

        // E2 host
        public ActionResult Build(Guid? id = null)
        {
            ViewBag.Title = id.HasValue ? "Editar reporte" : "Nuevo reporte";
            ViewBag.PageId = "reports";
            ViewBag.ReportId = id;
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Reportes", Url.Action("Index")),
                System.Tuple.Create("Reporte", (string)null)
            };
            return View();
        }

        // E3
        public async Task<ActionResult> Preview(Guid id, int? page = null)
        {
            ViewBag.Title = "Preview";
            ViewBag.PageId = "reports";
            ViewBag.ReportId = id;
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Reportes", Url.Action("Index")),
                System.Tuple.Create("Preview", (string)null)
            };

            var model = new ReportPreviewViewModel();
            try
            {
                var saved = await _saved.GetAsync(id);
                if (saved == null) return HttpNotFound();

                model.ReportName = saved.Name;

                var query = JsonConvert.DeserializeObject<ReportQuery>(saved.QueryJson ?? "{}")
                            ?? new ReportQuery();
                query.QuestionnaireId = saved.QuestionnaireId;
                if (page.HasValue && page.Value > 0) query.Page = page.Value;

                model.Page = query.Page;
                model.PageSize = query.PageSize;

                // Formato ancho (una fila por ficha), igual que /api/reports/query. Antes esta
                // pantalla usaba el resultado crudo y mostraba el formato largo: una fila por
                // respuesta-pregunta, con columnas ResponseId/LinkId/ValueDecimal, y un Total
                // inflado por el numero de preguntas.
                var result = await _executor.ExecuteAsync(query);
                model.Total = result.Total;

                if (result.Rows.Count > 0)
                    model.Columns = result.Rows[0].Keys.ToList();

                foreach (var row in result.Rows)
                {
                    var previewRow = new ReportPreviewRow();
                    foreach (var col in model.Columns)
                    {
                        row.TryGetValue(col, out var value);
                        previewRow.Cells[col] = value?.ToString() ?? "";
                    }
                    model.Rows.Add(previewRow);
                }
            }
            catch (Exception)
            {
                ViewBag.DataError = "Ocurrió un error al procesar la solicitud.";
            }
            return View(model);
        }

        // E5
        public async Task<ActionResult> Stats(Guid id)
        {
            ViewBag.Title = "Estadísticas";
            ViewBag.PageId = "reports";
            ViewBag.ReportId = id;
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Reportes", Url.Action("Index")),
                System.Tuple.Create("Estadísticas", (string)null)
            };

            var model = new ReportStatsViewModel { ReportId = id };
            try
            {
                var saved = await _saved.GetAsync(id);
                if (saved == null) return HttpNotFound();
                model.ReportName = saved.Name;

                // La query viaja a la isla tal cual, y la isla se la manda a /api/reports/stats:
                // los numeros salen de los mismos filtros que la tabla, no de un id suelto.
                var query = JsonConvert.DeserializeObject<ReportQuery>(saved.QueryJson ?? "{}") ?? new ReportQuery();
                query.QuestionnaireId = saved.QuestionnaireId;
                model.QueryJson = JsonConvert.SerializeObject(query, new JsonSerializerSettings
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver(),
                    NullValueHandling = NullValueHandling.Ignore
                });
            }
            catch (Exception)
            {
                ViewBag.DataError = "Ocurrió un error al procesar la solicitud.";
            }
            return View(model);
        }
    }
}
