using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using ChequeAR.Api.Models;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Api.Controllers.Mvc
{
    // TODO: re-habilitar [Authorize] cuando FormsAuth esté wired contra el hospital.
    public class QuestionnairesController : Controller
    {
        private readonly IQuestionnaireRepository _repo;
        private readonly QuestionnaireService _svc;

        public QuestionnairesController(IQuestionnaireRepository repo, QuestionnaireService svc)
        {
            _repo = repo;
            _svc = svc;
        }

        // Q1 — Listado
        public async Task<ActionResult> Index(string q = null, string specialty = null, string status = null, int page = 1)
        {
            ViewBag.Title = "Cuestionarios";
            ViewBag.PageId = "questionnaires";
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Cuestionarios", (string)null)
            };

            var model = new QuestionnaireListViewModel
            {
                SearchTerm = q, SpecialtyFilter = specialty, StatusFilter = status,
                Page = page, PageSize = 20
            };

            try
            {
                QuestionnaireStatus? st = null;
                if (Enum.TryParse(status, true, out QuestionnaireStatus s)) st = s;
                var items = await _repo.SearchAsync(st, q, specialty, (page - 1) * 20, 20);
                model.Total = await _repo.CountAsync(st, q, specialty);
                foreach (var it in items)
                {
                    model.Items.Add(new QuestionnaireListItem
                    {
                        Id = it.Id, Title = it.Title,
                        SpecialtyCode = it.SpecialtyCode ?? "general",
                        SpecialtyLabel = it.SpecialtyDisplay ?? it.SpecialtyCode ?? "General",
                        Version = it.Versions?.Count ?? 0,
                        Status = it.Status.ToString().ToLowerInvariant(),
                        UpdatedAt = it.UpdatedAt.ToString("yyyy-MM-dd"),
                        Author = it.CreatedBy
                    });
                }
            }
            catch
            {
                model.Items = MockSeed.Questionnaires();
                model.Total = model.Items.Count;
                ViewBag.DataMode = "demo";
            }
            return View(model);
        }

        // Q2 — Editor (host Vue)
        public async Task<ActionResult> Edit(Guid id)
        {
            ViewBag.Title = "Editor de cuestionario";
            ViewBag.PageId = "questionnaires";
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Cuestionarios", Url.Action("Index")),
                System.Tuple.Create("Editar", (string)null)
            };
            ViewBag.QuestionnaireId = id == Guid.Empty ? Guid.NewGuid() : id;
            try
            {
                var q = await _repo.GetAsync(id);
                ViewBag.QuestionnaireTitle = q?.Title ?? "Nuevo cuestionario";
                ViewBag.Version = (q?.Versions?.Count ?? 0) + 1;
            }
            catch
            {
                ViewBag.QuestionnaireTitle = "Cardio basal";
                ViewBag.Version = 3;
                ViewBag.DataMode = "demo";
            }
            return View();
        }

        // Q5 — Vista previa
        public async Task<ActionResult> Preview(Guid id)
        {
            ViewBag.Title = "Vista previa";
            ViewBag.PageId = "questionnaires";
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Cuestionarios", Url.Action("Index")),
                System.Tuple.Create("Vista previa", (string)null)
            };

            try
            {
                var q = await _repo.GetAsync(id);
                if (q == null) return View("~/Views/Error/Index.cshtml");
                var model = new QuestionnaireDetailViewModel
                {
                    Id = q.Id, Title = q.Title,
                    SpecialtyCode = q.SpecialtyCode ?? "general",
                    SpecialtyLabel = q.SpecialtyDisplay ?? q.SpecialtyCode ?? "General",
                    Version = q.Versions?.Count ?? 0,
                    Status = q.Status.ToString().ToLowerInvariant(),
                    Author = q.CreatedBy,
                    UpdatedAt = q.UpdatedAt.ToString("yyyy-MM-dd")
                };
                if (q.Questions != null)
                {
                    var group = new QuestionGroupView { Title = "Preguntas" };
                    foreach (var qq in q.Questions)
                    {
                        group.Questions.Add(new QuestionView
                        {
                            LinkId = qq.LinkId, Text = qq.Text,
                            Type = qq.Type.ToString().ToLowerInvariant(),
                            Required = qq.Required
                        });
                    }
                    model.Groups.Add(group);
                }
                return View(model);
            }
            catch
            {
                ViewBag.DataMode = "demo";
                return View(MockSeed.QuestionnaireDetail(id));
            }
        }

        // Q9 — Historial
        public async Task<ActionResult> History(Guid id)
        {
            ViewBag.Title = "Historial de versiones";
            ViewBag.PageId = "questionnaires";
            ViewBag.QuestionnaireId = id;
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Cuestionarios", Url.Action("Index")),
                System.Tuple.Create("Historial", (string)null)
            };
            try
            {
                var q = await _repo.GetAsync(id);
                ViewBag.QuestionnaireTitle = q?.Title;
                var list = (q?.Versions ?? new List<ChequeAR.Domain.Entities.QuestionnaireVersion>())
                    .OrderByDescending(v => v.VersionNumber)
                    .Select(v => new QuestionnaireVersionView
                    {
                        Version = v.VersionNumber,
                        Date = v.PublishedAt.ToString("yyyy-MM-dd"),
                        Author = v.PublishedBy,
                        Status = "published",
                        ChangeNote = ""
                    }).ToList();
                return View(list);
            }
            catch
            {
                ViewBag.QuestionnaireTitle = "Cardio basal";
                ViewBag.DataMode = "demo";
                return View(MockSeed.QuestionnaireVersions());
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<ActionResult> Publish(Guid id, string changeNote)
        {
            var userId = User?.Identity?.Name ?? "unknown";
            try { await _svc.PublishAsync(id, null, userId); TempData["Toast"] = "Cuestionario publicado."; }
            catch (Exception ex) { TempData["Toast"] = "Error: " + ex.Message; }
            return RedirectToAction("Index");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<ActionResult> Archive(Guid id)
        {
            var userId = User?.Identity?.Name ?? "unknown";
            try { await _svc.ArchiveAsync(id, userId); TempData["Toast"] = "Cuestionario archivado."; }
            catch (Exception ex) { TempData["Toast"] = "Error: " + ex.Message; }
            return RedirectToAction("Index");
        }
    }
}
