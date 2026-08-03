using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using ChequeAR.Api.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Api.Controllers.Mvc
{
    // TODO: re-habilitar [Authorize] cuando FormsAuth esté wired contra el hospital.
    public class QuestionnairesController : Controller
    {
        
        private readonly QuestionnaireService _svc;

        public QuestionnairesController(QuestionnaireService svc)
        {
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
                // Opciones del filtro derivadas de los cuestionarios reales (antes estaban hardcodeadas).
                foreach (var code in await _svc.GetSpecialtyCodesAsync())
                    model.Specialties.Add(new SpecialtyOption { Code = code, Label = SpecialtyMap.Label(code) });

                var st = QuestionnaireStatusMap.FromUi(status);
                var items = await _svc.SearchAsync(st, q, specialty, (page - 1) * 20, 20);
                model.Total = await _svc.CountAsync(st, q, specialty);
                foreach (var it in items)
                {
                    model.Items.Add(new QuestionnaireListItem
                    {
                        Id = it.Id, Title = it.Title,
                        SpecialtyCode = it.SpecialtyCode ?? "general",
                        SpecialtyLabel = SpecialtyMap.Label(it.SpecialtyCode, it.SpecialtyDisplay),
                        Version = it.Versions?.Count ?? 0,
                        Status = QuestionnaireStatusMap.ToUi(it.Status),
                        UpdatedAt = ChequeAR.Api.Helpers.ArgentinaTime.FormatDate(it.UpdatedAt),
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
                    var q = await _svc.GetAsync(id.Value);
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
                new { id = editorId, title, version = publishedVersion, isNew, specialtyCode = specialty, status, concurrencyVersion, groups, hasPendingChanges = pendingChanges },
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
                Type = QuestionTypeMap.ToUi(qq.Type),
                Required = qq.Required,
                Options = (qq.Options ?? new List<ChequeAR.Domain.Entities.AnswerOption>())
                    .OrderBy(o => o.OrderIndex)
                    .Select(o => o.Display ?? o.Value ?? o.Code)
                    .ToList()
            };

            // Desarmar los contratos JSON es conocimiento del dominio y vive en la capa de
            // negocio; acá sólo queda pasarlos al ViewModel, que sí es presentación.
            var validation = QuestionMetadataReader.ReadValidation(qq.ValidationJson);
            if (validation != null)
            {
                view.Min = validation.Min;
                view.Max = validation.Max;
                view.Unit = validation.Unit;
                view.Accept = validation.Accept;
                view.MaxLength = validation.MaxLength;
                view.Pattern = validation.Pattern;
            }

            var enableWhen = QuestionMetadataReader.ReadCondition(qq.EnableWhenJson);
            if (enableWhen != null)
            {
                view.EnableWhen = new EnableWhenView
                {
                    Question = enableWhen.Question,
                    Op = enableWhen.Op,
                    Value = enableWhen.Value
                };
            }

            var coding = QuestionMetadataReader.ReadCoding(qq.CodingJson);
            if (coding != null)
            {
                view.CodeSystem = coding.System;
                view.Code = coding.Code;
                view.CodeDisplay = coding.Display;
                view.Term = coding.Term;
            }
            return view;
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
                var q = await _svc.GetAsync(id);
                if (q == null) return View("~/Views/Error/Index.cshtml");
                var model = new QuestionnaireDetailViewModel
                {
                    Id = q.Id, Title = q.Title,
                    SpecialtyCode = q.SpecialtyCode ?? "general",
                    SpecialtyLabel = SpecialtyMap.Label(q.SpecialtyCode, q.SpecialtyDisplay),
                    Version = q.Versions?.Count ?? 0,
                    Status = QuestionnaireStatusMap.ToUi(q.Status),
                    Author = q.CreatedBy,
                    UpdatedAt = ChequeAR.Api.Helpers.ArgentinaTime.FormatDate(q.UpdatedAt),
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
                var q = await _svc.GetAsync(id);
                ViewBag.QuestionnaireTitle = q?.Title;
                var list = (q?.Versions ?? new List<ChequeAR.Domain.Entities.QuestionnaireVersion>())
                    .OrderByDescending(v => v.VersionNumber)
                    .Select(v => new QuestionnaireVersionView
                    {
                        Version = v.VersionNumber,
                        Date = ChequeAR.Api.Helpers.ArgentinaTime.FormatDate(v.PublishedAt),
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
            try
            {
                await _svc.PublishAsync(id, userId, changeNote);
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
