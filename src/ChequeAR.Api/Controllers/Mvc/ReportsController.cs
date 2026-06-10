using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Mvc;
using ChequeAR.Api.Models;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Reports;

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
            catch
            {
                model.Items = MockSeed.SavedReports();
                ViewBag.DataMode = "demo";
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
        public ActionResult Preview(Guid id)
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
            // El preview real lo ejecuta la isla Vue contra POST /api/reports/query.
            // Aquí mostramos un preview demo + columnas guardadas.
            ViewBag.DataMode = "demo";
            return View(MockSeed.ReportPreview());
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
