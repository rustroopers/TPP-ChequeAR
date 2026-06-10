using System.Collections.Generic;
using System.Web.Mvc;
using ChequeAR.Api.Models;

namespace ChequeAR.Api.Controllers.Mvc
{
    public class ReportsController : Controller
    {
        // E1 — Listado de reportes guardados
        public ActionResult Index()
        {
            ViewBag.Title = "Reportes";
            ViewBag.PageId = "reports";
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Reportes", (string)null)
            };
            var model = new ReportListViewModel { Items = MockSeed.SavedReports() };
            return View(model);
        }

        // E2 — Query Builder (host de isla Vue)
        public ActionResult Build(long? id = null)
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

        // E3 — Preview
        public ActionResult Preview(long id)
        {
            ViewBag.Title = "Preview de cohorte";
            ViewBag.PageId = "reports";
            ViewBag.ReportId = id;
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Reportes", Url.Action("Index")),
                System.Tuple.Create("Preview", (string)null)
            };
            return View(MockSeed.ReportPreview());
        }

        // E5 — Estadísticas (P3)
        public ActionResult Stats(long id)
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
