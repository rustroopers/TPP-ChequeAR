using System.Collections.Generic;
using System.Web.Mvc;
using ChequeAR.Api.Models;

namespace ChequeAR.Api.Controllers.Mvc
{
    public class AuditController : Controller
    {
        // A6 — Visor de auditoría
        public ActionResult Index(string user = null, string action = null, string resource = null, string from = null, string to = null, int page = 1)
        {
            ViewBag.Title = "Auditoría";
            ViewBag.PageId = "audit";
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Auditoría", (string)null)
            };

            var model = new AuditListViewModel
            {
                UserFilter = user,
                ActionFilter = action,
                ResourceFilter = resource,
                From = from,
                To = to,
                Page = page,
                Total = 1230,
                Entries = MockSeed.AuditEntries()
            };
            return View(model);
        }

        // A7 — Detalle de evento
        public ActionResult Details(long id)
        {
            ViewBag.Title = "Evento de auditoría";
            ViewBag.PageId = "audit";
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Auditoría", Url.Action("Index")),
                System.Tuple.Create("#" + id, (string)null)
            };
            return View(MockSeed.AuditDetail(id));
        }
    }
}
