using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using ChequeAR.Api.Models;
using ChequeAR.Application.Dtos;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Reports;
using Newtonsoft.Json;

namespace ChequeAR.Api.Controllers.Mvc
{
    // TODO: re-habilitar [Authorize] cuando FormsAuth esté wired contra el hospital.
    public class ReportsController : Controller
    {
        private readonly ISavedReportRepository _savedRepo;
        private readonly ReportExecutor _executor;

        public ReportsController(ISavedReportRepository savedRepo, ReportExecutor executor)
        {
            _savedRepo = savedRepo;
            _executor = executor;
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
                var userId = User?.Identity?.Name ?? "anonymous";
                var saved = await _savedRepo.ListForUserAsync(userId, new string[0]);
                foreach (var r in saved)
                {
                    model.Items.Add(new SavedReportItem
                    {
                        Id = r.Id, Name = r.Name,
                        QuestionnaireTitle = r.QuestionnaireId.ToString(),
                        Author = r.CreatedBy,
                        LastUsedAt = r.UpdatedAt.ToString("yyyy-MM-dd")
                    });
                }
            }
            catch (Exception ex)
            {
                model.Items.Clear();
                ViewBag.DataError = ex.Message;
            }
            return View(model);
        }

        // E2 host
        public ActionResult Build(Guid? id = null)
        {
            ViewBag.Title = id.HasValue ? "Editar consulta" : "Nueva consulta";
            ViewBag.PageId = "reports";
            ViewBag.ReportId = id;
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Reportes", Url.Action("Index")),
                System.Tuple.Create("Consulta", (string)null)
            };
            return View();
        }

        // E3
        public async Task<ActionResult> Preview(Guid id)
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
                var saved = await _savedRepo.GetAsync(id);
                if (saved == null) return HttpNotFound();

                model.ReportName = saved.Name;

                var query = JsonConvert.DeserializeObject<ReportQuery>(saved.QueryJson ?? "{}")
                            ?? new ReportQuery();
                query.QuestionnaireId = saved.QuestionnaireId;

                model.Page = query.Page;
                model.PageSize = query.PageSize;

                var result = await _executor.ExecuteAsync(query);
                model.Total = result.Total;

                // Las columnas salen del propio resultset: el QueryBuilder ya
                // proyecta solo lo que el reporte pidio.
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
            catch (Exception ex)
            {
                ViewBag.DataError = ex.Message;
            }
            return View(model);
        }

        // E5
        public ActionResult Stats(Guid id)
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
            return View();
        }
    }
}
