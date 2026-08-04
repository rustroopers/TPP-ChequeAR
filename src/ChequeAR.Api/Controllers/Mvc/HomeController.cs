using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Mvc;
using ChequeAR.Api.Models;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Services;

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
                var recent = await _qrRepo.SearchAsync(null, null, null, 0, 5);
                var titleCache = new Dictionary<System.Guid, ChequeAR.Domain.Entities.Questionnaire>();
                foreach (var r in recent)
                {
                    if (!titleCache.ContainsKey(r.QuestionnaireId))
                        titleCache[r.QuestionnaireId] = await _qRepo.GetAsync(r.QuestionnaireId);
                    var q = titleCache[r.QuestionnaireId];

                    // El estado de una QuestionnaireResponse no es "borrador vs publicado"
                    // (eso es del ciclo de vida de Questionnaire); acá reflejamos el ciclo de
                    // vida real de la ficha: InProgress se sigue completando con el tiempo
                    // (no es un borrador descartable), y solo esa va a "Continuar edición".
                    string status;
                    string openUrl;
                    switch (r.Status)
                    {
                        case ChequeAR.Domain.Enums.ResponseStatus.InProgress:
                            status = "pending";
                            openUrl = Url.Action("Load", "Encounters", new { patientId = r.PatientHospitalId, questionnaireId = r.QuestionnaireId });
                            break;
                        case ChequeAR.Domain.Enums.ResponseStatus.Amended:
                            status = "amended";
                            openUrl = Url.Action("View", "Encounters", new { id = r.Id });
                            break;
                        case ChequeAR.Domain.Enums.ResponseStatus.EnteredInError:
                            status = "error";
                            openUrl = Url.Action("View", "Encounters", new { id = r.Id });
                            break;
                        default:
                            status = "completed";
                            openUrl = Url.Action("View", "Encounters", new { id = r.Id });
                            break;
                    }

                    model.RecentEncounters.Add(new RecentEncounter
                    {
                        PatientName = ChequeAR.Application.Services.FhirPatientReader.ReadName(r.FhirJson) ?? r.PatientHospitalId,
                        QuestionnaireTitle = q?.Title ?? r.QuestionnaireId.ToString(),
                        SpecialtyCode = q?.SpecialtyCode ?? "general",
                        SpecialtyLabel = SpecialtyMap.Label(q?.SpecialtyCode, q?.SpecialtyDisplay),
                        Status = status,
                        UpdatedAt = ChequeAR.Api.Helpers.ArgentinaTime.FormatDateTime(r.LastSavedAt),
                        OpenUrl = openUrl
                    });
                }

                if (model.IsAdmin)
                {
                    var totalQ = await _qRepo.CountAsync(null, null, null);
                    var totalU = await _userRepo.CountAsync(null, null, null, true);
                    var totalE = (await _qrRepo.SearchAsync(null, null, null, 0, 1000)).Count;
                    model.Kpis = new KpiSummary { Questionnaires = totalQ, Encounters = totalE, Users = totalU, Specialties = 4 };
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
