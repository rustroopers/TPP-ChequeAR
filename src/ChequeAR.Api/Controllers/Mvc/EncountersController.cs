using System;
using System.Collections.Generic;
using System.Linq;
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
            catch (Exception ex)
            {
                model.Results.Clear();
                ViewBag.DataError = ex.Message;
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
            catch (Exception ex)
            {
                ViewBag.PatientName = "HC " + patientId;
                ViewBag.QuestionnaireTitle = null;
                ViewBag.Version = 0;
                ViewBag.DataError = ex.Message;
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

                // Respuestas: unir los items cargados con las preguntas del cuestionario.
                var itemsByLink = (qr.Items ?? new List<ChequeAR.Domain.Entities.QuestionnaireResponseItem>())
                    .GroupBy(i => i.LinkId).ToDictionary(gr => gr.Key, gr => gr.First());
                var answered = new QuestionGroupAnswered { Title = "Respuestas" };
                var questions = (q?.Questions ?? new List<ChequeAR.Domain.Entities.Question>()).OrderBy(x => x.OrderIndex);
                foreach (var question in questions)
                {
                    itemsByLink.TryGetValue(question.LinkId, out var item);
                    answered.Questions.Add(new AnsweredQuestion
                    {
                        Text = question.Text,
                        Answer = FormatAnswer(item),
                        Prefilled = item?.WasPrefilled ?? false
                    });
                }
                if (answered.Questions.Count > 0) model.Groups.Add(answered);
                return base.View(model);
            }
            catch (Exception ex)
            {
                ViewBag.DataError = ex.Message;
                return base.View(new EncounterReadonlyViewModel { EncounterId = id });
            }
        }

        private static string FormatAnswer(ChequeAR.Domain.Entities.QuestionnaireResponseItem item)
        {
            if (item == null) return "—";
            if (item.ValueBoolean.HasValue) return item.ValueBoolean.Value ? "Sí" : "No";
            if (item.ValueInteger.HasValue) return item.ValueInteger.Value.ToString();
            if (item.ValueDecimal.HasValue) return item.ValueDecimal.Value.ToString();
            if (item.ValueDate.HasValue) return item.ValueDate.Value.ToString("yyyy-MM-dd");
            if (!string.IsNullOrEmpty(item.ValueString)) return item.ValueString;
            if (!string.IsNullOrEmpty(item.ValueCode)) return item.ValueCode;
            return "—";
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
                var titleCache = new Dictionary<Guid, ChequeAR.Domain.Entities.Questionnaire>();
                foreach (var e in encs)
                {
                    if (!titleCache.ContainsKey(e.QuestionnaireId))
                        titleCache[e.QuestionnaireId] = await _qRepo.GetAsync(e.QuestionnaireId);
                    var q = titleCache[e.QuestionnaireId];
                    model.Entries.Add(new EncounterTimelineItem
                    {
                        Date = e.AuthoredAt.ToString("yyyy-MM-dd"),
                        QuestionnaireTitle = q?.Title ?? e.QuestionnaireId.ToString(),
                        QuestionnaireVersion = q?.Versions?.Count ?? 0,
                        Author = e.AuthorUserId,
                        EncounterId = e.Id,
                        Status = e.Status.ToString().ToLowerInvariant()
                    });
                }
                return View(model);
            }
            catch (Exception ex)
            {
                ViewBag.DataError = ex.Message;
                return View(new PatientTimelineViewModel
                {
                    Patient = new PatientSearchItem { HC = patientId }
                });
            }
        }
    }
}
