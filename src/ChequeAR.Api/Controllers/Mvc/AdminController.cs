using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Mvc;
using ChequeAR.Api.Models;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Api.Controllers.Mvc
{
    // TODO: re-habilitar [Authorize(Roles = Role.Administrador + "," + Role.Jefe)] cuando FormsAuth esté wired.
    public class AdminController : Controller
    {
        private readonly IntegrationHealthService _health;
        public AdminController(IntegrationHealthService health) { _health = health; }

        // F1
        public async Task<ActionResult> Integrations()
        {
            ViewBag.Title = "Integraciones";
            ViewBag.PageId = "integrations";
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Integraciones", (string)null)
            };
            var model = new IntegrationsViewModel();
            try
            {
                var probes = await _health.ProbeAllAsync();
                foreach (var p in probes)
                {
                    model.Integrations.Add(new Models.IntegrationStatus
                    {
                        Name = p.Name,
                        Status = p.Status,
                        LastPing = p.LastPingUtc.ToLocalTime().ToString("HH:mm:ss"),
                        LatencyP95 = p.LatencyMs + " ms",
                        Detail = p.Detail
                    });
                }
            }
            catch
            {
                model = MockSeed.IntegrationsStatus();
                ViewBag.DataMode = "demo";
            }
            return View(model);
        }

        // F2 (estática)
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
