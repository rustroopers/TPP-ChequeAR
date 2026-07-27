using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using ChequeAR.Api.Models;

namespace ChequeAR.Api.Controllers.Mvc
{
    // TODO: re-habilitar [Authorize(Roles = Role.Administrador + "," + Role.Jefe)] cuando FormsAuth esté wired.
    public class AdminController : Controller
    {
        // F2 (estática)
        public ActionResult Capability()
        {
            ViewBag.Title = "CapabilityStatement";
            ViewBag.PageId = "capability";
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
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
