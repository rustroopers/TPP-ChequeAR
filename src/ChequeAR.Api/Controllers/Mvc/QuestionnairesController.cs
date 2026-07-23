using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using ChequeAR.Api.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
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
                var st = QuestionnaireStatusMap.FromUi(status);
                var items = await _repo.SearchAsync(st, q, specialty, (page - 1) * 20, 20);
                model.Total = await _repo.CountAsync(st, q, specialty);
                foreach (var it in items)
                {
                    model.Items.Add(new QuestionnaireListItem
                    {
                        Id = it.Id, Title = it.Title,
                        SpecialtyCode = it.SpecialtyCode ?? "general",
                        SpecialtyLabel = SpecialtyMap.Label(it.SpecialtyCode, it.SpecialtyDisplay),
                        Version = it.Versions?.Count ?? 0,
                        Status = QuestionnaireStatusMap.ToUi(it.Status),
                        UpdatedAt = it.UpdatedAt.ToString("yyyy-MM-dd"),
                        Author = it.CreatedBy,
                        HasUnpublishedChanges = it.HasUnpublishedChanges()
                    });
                }
            }
            catch (Exception ex)
            {
                model.Items.Clear();
                model.Total = 0;
                ViewBag.DataError = ex.Message;
            }
            return View(model);
        }

        // Q2 — Editor (host Vue). id null = nuevo cuestionario.
        public async Task<ActionResult> Edit(Guid? id = null)
        {
            ViewBag.Title = "Editor de cuestionario";
            ViewBag.PageId = "questionnaires";
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Cuestionarios", Url.Action("Index")),
                System.Tuple.Create(id.HasValue ? "Editar" : "Nuevo", (string)null)
            };
            var editorId = id ?? Guid.NewGuid();
            var isNew = !id.HasValue;
            var title = "Nuevo cuestionario";
            var specialty = "general";
            // Número de la versión PUBLICADA (0 = nunca se publicó), no el de la próxima:
            // el editor tiene que decir cuál es la que están usando los clínicos.
            var publishedVersion = 0;
            var status = "draft";
            var pendingChanges = false;
            var concurrencyVersion = 0;
            var groups = new List<QuestionGroupView>();

            if (id.HasValue)
            {
                try
                {
                    var q = await _repo.GetAsync(id.Value);
                    title = q?.Title ?? "Cuestionario";
                    if (q != null)
                    {
                        publishedVersion = CurrentVersionNumber(q);
                        specialty = q.SpecialtyCode ?? "general";
                        status = QuestionnaireStatusMap.ToUi(q.Status);
                        pendingChanges = q.HasUnpublishedChanges();
                        concurrencyVersion = q.ConcurrencyVersion;
                        // El editor se hidrata con la estructura real desde el server; antes
                        // arrancaba con datos de ejemplo hardcodeados y pisaba lo guardado.
                        groups = BuildEditorGroups(q);
                    }
                }
                catch (Exception ex)
                {
                    title = null;
                    publishedVersion = 0;
                    ViewBag.DataError = ex.Message;
                }
            }

            ViewBag.QuestionnaireId = editorId;
            ViewBag.IsNew = isNew;
            ViewBag.QuestionnaireTitle = title;
            ViewBag.Version = publishedVersion;
            ViewBag.NextVersion = publishedVersion + 1;
            ViewBag.Status = status;
            ViewBag.HasUnpublishedChanges = pendingChanges;
            // El JSON de props se arma acá (no en la vista) para no serializar dynamics.
            // status manda: el editor no autoguarda un cuestionario publicado.
            ViewBag.EditorProps = JsonConvert.SerializeObject(
                new { id = editorId, title, version = publishedVersion, isNew, specialtyCode = specialty, status, concurrencyVersion, groups },
                new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() });
            return View();
        }

        // Versión viva: la que apunta CurrentVersionId. Si por lo que sea quedó colgado,
        // caemos a la más alta publicada. 0 = nunca se publicó.
        private static int CurrentVersionNumber(ChequeAR.Domain.Entities.Questionnaire q)
        {
            if (q.Versions == null || q.Versions.Count == 0) return 0;
            if (q.CurrentVersionId.HasValue)
            {
                var current = q.Versions.FirstOrDefault(v => v.Id == q.CurrentVersionId.Value);
                if (current != null) return current.VersionNumber;
            }
            return q.Versions.Max(v => v.VersionNumber);
        }

        // Arma el árbol secciones -> preguntas que consume la isla Vue. Las preguntas sin
        // sección se juntan en una sección implícita para que sean editables igual.
        private static List<QuestionGroupView> BuildEditorGroups(ChequeAR.Domain.Entities.Questionnaire q)
        {
            var result = new List<QuestionGroupView>();
            var questions = q.Questions ?? new List<ChequeAR.Domain.Entities.Question>();

            foreach (var g in (q.Groups ?? new List<ChequeAR.Domain.Entities.QuestionGroup>()).OrderBy(g => g.OrderIndex))
            {
                var view = new QuestionGroupView { Title = g.Title };
                foreach (var qq in questions.Where(x => x.GroupId == g.Id).OrderBy(x => x.OrderIndex))
                    view.Questions.Add(ToQuestionView(qq));
                result.Add(view);
            }

            var loose = questions.Where(x => !x.GroupId.HasValue).OrderBy(x => x.OrderIndex).ToList();
            if (loose.Count > 0)
            {
                var view = new QuestionGroupView { Title = "Preguntas" };
                foreach (var qq in loose) view.Questions.Add(ToQuestionView(qq));
                result.Add(view);
            }
            return result;
        }

        private static QuestionView ToQuestionView(ChequeAR.Domain.Entities.Question qq)
        {
            var view = new QuestionView
            {
                LinkId = qq.LinkId,
                Text = qq.Text,
                Type = qq.Type.ToString().ToLowerInvariant(),
                Required = qq.Required,
                Options = (qq.Options ?? new List<ChequeAR.Domain.Entities.AnswerOption>())
                    .OrderBy(o => o.OrderIndex)
                    .Select(o => o.Display ?? o.Value ?? o.Code)
                    .ToList()
            };

            // min/max/unidad viajan dentro de ValidationJson; el editor las necesita de
            // vuelta como campos sueltos para poder mostrarlas en el panel de propiedades.
            var validation = ParseValidation(qq.ValidationJson);
            if (validation != null)
            {
                view.Min = (decimal?)validation["min"];
                view.Max = (decimal?)validation["max"];
                view.Unit = (string)validation["unit"];
                view.Accept = (string)validation["accept"];
                view.MaxLength = (int?)validation["maxLength"];
                view.Pattern = (string)validation["pattern"];
            }

            var enableWhen = ParseValidation(qq.EnableWhenJson);
            if (enableWhen != null && enableWhen["question"] != null)
            {
                view.EnableWhen = new EnableWhenView
                {
                    Question = (string)enableWhen["question"],
                    Op = (string)enableWhen["op"] ?? "=",
                    Value = ((JValue)enableWhen["value"])?.Value
                };
            }

            var coding = ParseValidation(qq.CodingJson);
            if (coding != null)
            {
                view.CodeSystem = (string)coding["system"];
                view.Code = (string)coding["code"];
                view.CodeDisplay = (string)coding["display"];
            }
            return view;
        }

        // Un ValidationJson corrupto no debe tumbar la pantalla: se ignora y la pregunta
        // se muestra sin restricciones.
        private static JObject ParseValidation(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JObject.Parse(json); }
            catch (JsonException) { return null; }
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
                    SpecialtyLabel = SpecialtyMap.Label(q.SpecialtyCode, q.SpecialtyDisplay),
                    Version = q.Versions?.Count ?? 0,
                    Status = QuestionnaireStatusMap.ToUi(q.Status),
                    Author = q.CreatedBy,
                    UpdatedAt = q.UpdatedAt.ToString("yyyy-MM-dd"),
                    HasUnpublishedChanges = q.HasUnpublishedChanges()
                };
                model.Groups.AddRange(BuildEditorGroups(q));
                return View(model);
            }
            catch (Exception ex)
            {
                ViewBag.DataError = ex.Message;
                return View(new QuestionnaireDetailViewModel { Id = id });
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
                        ChangeNote = v.ChangeNote
                    }).ToList();
                return View(list);
            }
            catch (Exception ex)
            {
                ViewBag.QuestionnaireTitle = null;
                ViewBag.DataError = ex.Message;
                return View(new List<QuestionnaireVersionView>());
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<ActionResult> Publish(Guid id, string changeNote)
        {
            var userId = User?.Identity?.Name ?? "unknown";
            // fhirJson null => el service lo arma desde la base (ver FhirQuestionnaireBuilder).
            try
            {
                await _svc.PublishAsync(id, null, userId, changeNote);
                TempData["Toast"] = "Cuestionario publicado.";
                TempData["ToastLevel"] = "success";
            }
            catch (Exception ex)
            {
                TempData["Toast"] = "No se pudo publicar: " + ex.Message;
                TempData["ToastLevel"] = "error";
            }
            return RedirectToAction("Index");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<ActionResult> Archive(Guid id)
        {
            var userId = User?.Identity?.Name ?? "unknown";
            try
            {
                await _svc.ArchiveAsync(id, userId);
                TempData["Toast"] = "Cuestionario archivado.";
                TempData["ToastLevel"] = "success";
            }
            catch (Exception ex)
            {
                TempData["Toast"] = "No se pudo archivar: " + ex.Message;
                TempData["ToastLevel"] = "error";
            }
            return RedirectToAction("Index");
        }
    }
}
