using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Mvc;
using ChequeAR.Api.Models;
using ChequeAR.Application.Services;

namespace ChequeAR.Api.Controllers.Mvc
{
    public class HomeController : Controller
    {
        private readonly HomeDashboardService _dashboard;

        public HomeController(HomeDashboardService dashboard)
        {
            _dashboard = dashboard;
        }

        public async Task<ActionResult> Index()
        {
            ViewBag.Title = "Inicio";
            ViewBag.PageId = "home";
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", (string)null)
            };

            var name = User != null && User.Identity != null && User.Identity.IsAuthenticated
                ? User.Identity.Name
                : "Doctor";

            var model = new HomeViewModel
            {
                UserGreeting = ChequeAR.Api.Helpers.Cq.T(ChequeAR.Api.Helpers.Cq.TimeGreetingKey(), new { name = name }),
                IsAdmin = User != null && User.IsInRole(ChequeAR.Domain.Enums.Role.Administrador),
                RecentEncounters = new List<RecentEncounter>(),
                Kpis = null
            };

            // Si la DB no responde la pantalla queda vacia con el banner de error;
            // nunca se rellena con datos inventados.
            try
            {
                // Los datos los arma el servicio; acá sólo queda darles forma de pantalla.
                foreach (var e in await _dashboard.RecentEncountersAsync(5))
                {
                    model.RecentEncounters.Add(new RecentEncounter
                    {
                        PatientName = e.PatientName,
                        QuestionnaireTitle = e.QuestionnaireTitle,
                        SpecialtyCode = e.SpecialtyCode,
                        SpecialtyLabel = SpecialtyMap.Label(e.SpecialtyCode, e.SpecialtyDisplay),
                        Status = e.Completed ? "published" : "draft",
                        UpdatedAt = ChequeAR.Api.Helpers.ArgentinaTime.FormatDateTime(e.LastSavedAt),
                        OpenUrl = Url.Action("View", "Encounters", new { id = e.Id })
                    });
                }

                if (model.IsAdmin)
                {
                    var k = await _dashboard.KpisAsync();
                    model.Kpis = new KpiSummary
                    {
                        Questionnaires = k.Questionnaires,
                        Encounters = k.Encounters,
                        Users = k.Users,
                        // Antes estaba fijo en 4; ahora sale de las especialidades reales.
                        Specialties = k.Specialties
                    };
                }
            }
            catch (Exception ex)
            {
                model.RecentEncounters.Clear();
                model.Kpis = null;
                ViewBag.DataError = ex.Message;
            }

            return View(model);
        }
    }
}
