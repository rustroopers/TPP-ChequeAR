using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Mvc;
using ChequeAR.Api.Models;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Services;

namespace ChequeAR.Api.Controllers.Mvc
{
    // TODO: re-habilitar [Authorize] cuando FormsAuth esté wired contra el hospital.
    public class EncountersController : Controller
    {
        private readonly PatientService _patients;
        private readonly IQuestionnaireResponseRepository _qrRepo;
        private readonly IQuestionnaireRepository _qRepo;

        public EncountersController(PatientService patients, IQuestionnaireResponseRepository qrRepo, IQuestionnaireRepository qRepo)
        {
            _patients = patients;
            _qrRepo = qrRepo;
            _qRepo = qRepo;
        }

        // R1
        public async Task<ActionResult> Search(string q = null)
        {
            ViewBag.Title = "Pacientes";
            ViewBag.PageId = "encounters";
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Pacientes", (string)null)
            };

            var model = new PatientSearchViewModel { Query = q, HasSearched = !string.IsNullOrWhiteSpace(q) };
            if (!model.HasSearched) return View(model);

            try
            {
                var hits = await _patients.SearchAsync(q);
                foreach (var h in hits)
                {
                    model.Results.Add(new PatientSearchItem
                    {
                        HC = h.HospitalId, FullName = h.FullName,
                        AgeLabel = h.AgeLabel,
                        SpecialtyCode = h.SpecialtyCode ?? "general",
                        SpecialtyLabel = h.SpecialtyLabel ?? "General"
                    });
                }
            }
            catch
            {
                model.Results = MockSeed.Patients();
                ViewBag.DataMode = "demo";
            }
            return View(model);
        }

        // R3 — Render (host Vue)
        public async Task<ActionResult> Load(string patientId, Guid questionnaireId)
        {
            ViewBag.Title = "Carga de ficha";
            ViewBag.PageId = "encounters";
            ViewBag.PatientId = patientId;
            ViewBag.QuestionnaireId = questionnaireId;
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Pacientes", Url.Action("Search")),
                System.Tuple.Create("Carga de ficha", (string)null)
            };

            try
            {
                var p = await _patients.GetAsync(patientId);
                ViewBag.PatientName = p != null ? (p.FullName + " (HC " + p.HospitalId + ")") : ("HC " + patientId);
                var q = await _qRepo.GetAsync(questionnaireId);
                ViewBag.QuestionnaireTitle = q?.Title ?? "Cuestionario";
                ViewBag.Version = q?.Versions?.Count ?? 0;
            }
            catch
            {
                ViewBag.PatientName = "García, Ana (HC " + patientId + ")";
                ViewBag.QuestionnaireTitle = "Cardio basal";
                ViewBag.Version = 3;
                ViewBag.DataMode = "demo";
            }
            return View();
        }

        // R6 — Read-only
        public async Task<ActionResult> View(Guid id)
        {
            ViewBag.Title = "Ficha";
            ViewBag.PageId = "encounters";
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Pacientes", Url.Action("Search")),
                System.Tuple.Create("Ficha", (string)null)
            };
            try
            {
                var qr = await _qrRepo.GetAsync(id);
                if (qr == null) return HttpNotFound();
                var q = await _qRepo.GetAsync(qr.QuestionnaireId);
                var p = await _patients.GetAsync(qr.PatientHospitalId);
                var model = new EncounterReadonlyViewModel
                {
                    EncounterId = qr.Id,
                    Patient = new PatientSearchItem { HC = qr.PatientHospitalId, FullName = p?.FullName, AgeLabel = p?.AgeLabel, SpecialtyCode = p?.SpecialtyCode ?? "general", SpecialtyLabel = p?.SpecialtyLabel ?? "General" },
                    QuestionnaireTitle = q?.Title,
                    QuestionnaireVersion = q?.Versions?.Count ?? 0,
                    Author = qr.AuthorUserId,
                    FinalizedAt = qr.LastSavedAt.ToString("yyyy-MM-dd HH:mm")
                };
                return base.View(model);
            }
            catch
            {
                ViewBag.DataMode = "demo";
                return base.View(MockSeed.EncounterReadonly(id));
            }
        }

        // R7 — Timeline
        public async Task<ActionResult> Timeline(string patientId)
        {
            ViewBag.Title = "Historial del paciente";
            ViewBag.PageId = "encounters";
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Pacientes", Url.Action("Search")),
                System.Tuple.Create("Historial", (string)null)
            };
            try
            {
                var p = await _patients.GetAsync(patientId);
                var encs = await _patients.GetEncountersAsync(patientId, 50);
                var model = new PatientTimelineViewModel
                {
                    Patient = new PatientSearchItem { HC = patientId, FullName = p?.FullName, AgeLabel = p?.AgeLabel, SpecialtyCode = p?.SpecialtyCode ?? "general", SpecialtyLabel = p?.SpecialtyLabel ?? "General" }
                };
                foreach (var e in encs)
                {
                    model.Entries.Add(new EncounterTimelineItem
                    {
                        Date = e.AuthoredAt.ToString("yyyy-MM-dd"),
                        QuestionnaireTitle = e.QuestionnaireId.ToString(),
                        QuestionnaireVersion = 0,
                        Author = e.AuthorUserId,
                        EncounterId = e.Id,
                        Status = e.Status.ToString().ToLowerInvariant()
                    });
                }
                return View(model);
            }
            catch
            {
                ViewBag.DataMode = "demo";
                return View(MockSeed.PatientTimeline(patientId));
            }
        }
    }
}
