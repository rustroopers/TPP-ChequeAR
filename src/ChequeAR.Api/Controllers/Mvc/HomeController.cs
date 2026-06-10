using System.Collections.Generic;
using System.Web.Mvc;
using ChequeAR.Api.Models;

namespace ChequeAR.Api.Controllers.Mvc
{
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            ViewBag.Title = "Inicio";
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", (string)null)
            };

            // Mock view model — en producción viene de servicios de Application layer.
            var name = User != null && User.Identity != null && User.Identity.IsAuthenticated
                ? User.Identity.Name
                : "Doctora";
            var model = new HomeViewModel
            {
                UserGreeting = "Buen día, " + name,
                IsAdmin = true,
                RecentEncounters = new List<RecentEncounter>
                {
                    new RecentEncounter { PatientName = "García, Ana",  QuestionnaireTitle = "Cardio basal",   SpecialtyCode = "cardio",  Status = "published", UpdatedAt = "hoy 10:23", OpenUrl = "#" },
                    new RecentEncounter { PatientName = "López, Mateo", QuestionnaireTitle = "Neumo control",  SpecialtyCode = "neumo",   Status = "draft",     UpdatedAt = "ayer",      OpenUrl = "#" },
                    new RecentEncounter { PatientName = "Pérez, Lucía", QuestionnaireTitle = "Onco seguimiento", SpecialtyCode = "onco",  Status = "published", UpdatedAt = "ayer",      OpenUrl = "#" }
                },
                Kpis = new KpiSummary { Questionnaires = 47, Encounters = 1230, Users = 85, Specialties = 4 }
            };
            return View(model);
        }
    }
}
