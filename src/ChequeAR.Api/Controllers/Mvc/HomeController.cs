using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Mvc;
using ChequeAR.Api.Models;
using ChequeAR.Application.Services;

namespace ChequeAR.Api.Controllers.Mvc
{
    [System.Web.Mvc.Authorize]
    public class HomeController : Controller
    {
        private readonly HomeDashboardService _dashboard;
        private readonly SpecialtyCatalogService _specialties;

        public HomeController(HomeDashboardService dashboard, SpecialtyCatalogService specialties)
        {
            _dashboard = dashboard;
            _specialties = specialties;
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
                var cat = await _specialties.GetAsync();
                // Los datos los arma el servicio; acá sólo queda darles forma de pantalla.
                foreach (var e in await _dashboard.RecentEncountersAsync(5))
                {
                    // El estado de una QuestionnaireResponse no es "borrador vs publicado"
                    // (eso es del ciclo de vida de Questionnaire); acá reflejamos el ciclo de
                    // vida real de la ficha: InProgress se sigue completando con el tiempo
                    // (no es un borrador descartable), y solo esa va a "Continuar edición".
                    string status;
                    string openUrl;
                    switch (e.Status)
                    {
                        case ChequeAR.Domain.Enums.ResponseStatus.InProgress:
                            status = "pending";
                            openUrl = Url.Action("Load", "Encounters", new { patientId = e.PatientHospitalId, questionnaireId = e.QuestionnaireId });
                            break;
                        case ChequeAR.Domain.Enums.ResponseStatus.Amended:
                            status = "amended";
                            openUrl = Url.Action("View", "Encounters", new { id = e.Id });
                            break;
                        case ChequeAR.Domain.Enums.ResponseStatus.EnteredInError:
                            // Borrador descartado ($abandon): no es un error del sistema.
                            status = "discarded";
                            openUrl = Url.Action("View", "Encounters", new { id = e.Id });
                            break;
                        default:
                            status = "completed";
                            openUrl = Url.Action("View", "Encounters", new { id = e.Id });
                            break;
                    }

                    model.RecentEncounters.Add(new RecentEncounter
                    {
                        PatientName = e.PatientName,
                        QuestionnaireTitle = e.QuestionnaireTitle,
                        SpecialtyCode = cat.CodeOrDefault(e.SpecialtyCode),
                        SpecialtyLabel = cat.Label(e.SpecialtyCode, e.SpecialtyDisplay),
                        SpecialtyColor = cat.Color(e.SpecialtyCode),
                        Status = status,
                        UpdatedAt = ChequeAR.Api.Helpers.ArgentinaTime.FormatDateTime(e.LastSavedAt),
                        OpenUrl = openUrl
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
            catch (Exception)
            {
                model.RecentEncounters.Clear();
                model.Kpis = null;
                ViewBag.DataError = "Ocurrió un error al procesar la solicitud.";
            }

            return View(model);
        }
    }
}
