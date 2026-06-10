using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Mvc;
using ChequeAR.Api.Models;
using ChequeAR.Application.Interfaces;

namespace ChequeAR.Api.Controllers.Mvc
{
    public class HomeController : Controller
    {
        private readonly IQuestionnaireResponseRepository _qrRepo;
        private readonly IQuestionnaireRepository _qRepo;
        private readonly IUserRepository _userRepo;

        public HomeController(IQuestionnaireResponseRepository qrRepo, IQuestionnaireRepository qRepo, IUserRepository userRepo)
        {
            _qrRepo = qrRepo;
            _qRepo = qRepo;
            _userRepo = userRepo;
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
                : "Doctora";

            var model = new HomeViewModel
            {
                UserGreeting = "Buen día, " + name,
                IsAdmin = User != null && User.IsInRole(ChequeAR.Domain.Enums.Role.Administrador),
                RecentEncounters = new List<RecentEncounter>(),
                Kpis = null
            };

            // Datos reales con fallback a MockSeed si DB no responde.
            try
            {
                var recent = await _qrRepo.SearchAsync(null, null, null, 0, 5);
                foreach (var r in recent)
                {
                    model.RecentEncounters.Add(new RecentEncounter
                    {
                        PatientName = r.PatientHospitalId,
                        QuestionnaireTitle = r.QuestionnaireId.ToString(),
                        SpecialtyCode = "general",
                        Status = r.Status == ChequeAR.Domain.Enums.ResponseStatus.Completed ? "published" : "draft",
                        UpdatedAt = r.LastSavedAt.ToString("yyyy-MM-dd HH:mm"),
                        OpenUrl = Url.Action("View", "Encounters", new { id = r.Id })
                    });
                }

                if (model.IsAdmin)
                {
                    var totalQ = await _qRepo.CountAsync(null, null, null);
                    var totalU = await _userRepo.CountAsync(null, null, null, true);
                    model.Kpis = new KpiSummary { Questionnaires = totalQ, Encounters = 0, Users = totalU, Specialties = 4 };
                }
            }
            catch
            {
                // Fallback demo
                model.RecentEncounters = new List<RecentEncounter>
                {
                    new RecentEncounter { PatientName = "García, Ana",  QuestionnaireTitle = "Cardio basal",   SpecialtyCode = "cardio", Status = "published", UpdatedAt = "hoy 10:23", OpenUrl = "#" },
                    new RecentEncounter { PatientName = "López, Mateo", QuestionnaireTitle = "Neumo control",  SpecialtyCode = "neumo",  Status = "draft",     UpdatedAt = "ayer",      OpenUrl = "#" },
                    new RecentEncounter { PatientName = "Pérez, Lucía", QuestionnaireTitle = "Onco seguimiento", SpecialtyCode = "onco", Status = "published", UpdatedAt = "ayer",      OpenUrl = "#" }
                };
                if (model.IsAdmin)
                {
                    model.Kpis = new KpiSummary { Questionnaires = 47, Encounters = 1230, Users = 85, Specialties = 4 };
                }
                ViewBag.DataMode = "demo";
            }

            return View(model);
        }
    }
}
