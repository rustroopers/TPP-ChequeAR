using System;
using System.Collections.Generic;
using System.Linq;
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
            catch (Exception ex)
            {
                model.Integrations.Clear();
                model.RecentErrors.Clear();
                ViewBag.DataError = ex.Message;
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
            // Proyecta el mismo catalogo que sirve GET /fhir/r5/metadata.
            return View(new CapabilityStatementViewModel
            {
                FhirVersion = Fhir.CapabilityCatalog.FhirVersion,
                Software = Fhir.CapabilityCatalog.SoftwareName + " " + Fhir.CapabilityCatalog.SoftwareVersion,
                Implementation = Request.Url != null ? Request.Url.GetLeftPart(UriPartial.Authority) : null,
                Resources = Fhir.CapabilityCatalog.Resources
                    .Select(r => new CapResource
                    {
                        Type = r.Type,
                        Interactions = string.Join(" · ", r.Interactions)
                    })
                    .ToList()
            });
        }
    }
}
