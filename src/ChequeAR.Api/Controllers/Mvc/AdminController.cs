using System.Collections.Generic;
using System.Web.Mvc;
using ChequeAR.Api.Models;

namespace ChequeAR.Api.Controllers.Mvc
{
    public class AdminController : Controller
    {
        // F1 — Panel de salud de integraciones
        public ActionResult Integrations()
        {
            ViewBag.Title = "Integraciones";
            ViewBag.PageId = "integrations";
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Integraciones", (string)null)
            };
            return View(MockSeed.IntegrationsStatus());
        }

        // F2 — Visor CapabilityStatement
        public ActionResult Capability()
        {
            ViewBag.Title = "CapabilityStatement";
            ViewBag.PageId = "integrations";
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Integraciones", Url.Action("Integrations")),
                System.Tuple.Create("CapabilityStatement", (string)null)
            };
            return View(MockSeed.Capability());
        }
    }
}
