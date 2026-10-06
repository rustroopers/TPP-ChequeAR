using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using ChequeAR.Api.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using ChequeAR.Api.Auth;
using ChequeAR.Application.Exceptions;
using ChequeAR.Application.Security;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Api.Controllers.Mvc
{
    [Authorize]
    public class QuestionnairesController : Controller
    {
        
        private readonly QuestionnaireService _svc;
        private readonly SpecialtyCatalogService _specialties;
        private readonly ClinicalVariableService _variables;

        public QuestionnairesController(QuestionnaireService svc, SpecialtyCatalogService specialties,
            ClinicalVariableService variables)
        {
            _svc = svc;
            _specialties = specialties;
            _variables = variables;
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
                var cat = await _specialties.GetAsync();
                // Opciones del filtro derivadas de los cuestionarios que el usuario puede ver
                // (antes estaban hardcodeadas, y antes de eso mostraban especialidades ajenas).
                foreach (var code in await _svc.GetSpecialtyCodesAsync())
                    model.Specialties.Add(new SpecialtyOption { Code = code, Label = cat.Label(code) });

                var st = QuestionnaireStatusMap.FromUi(status);
                var items = await _svc.SearchAsync(st, q, specialty, (page - 1) * 20, 20);
                model.Total = await _svc.CountAsync(st, q, specialty);
                foreach (var it in items)
                {
                    model.Items.Add(new QuestionnaireListItem
                    {
                        Id = it.Id, Title = it.Title,
                        SpecialtyCode = cat.CodeOrDefault(it.SpecialtyCode),
                        SpecialtyLabel = cat.Label(it.SpecialtyCode, it.SpecialtyDisplay),
                        SpecialtyColor = cat.Color(it.SpecialtyCode),
                        Version = it.Versions?.Count ?? 0,
                        Status = QuestionnaireStatusMap.ToUi(it.Status),
                        UpdatedAt = ChequeAR.Api.Helpers.ArgentinaTime.FormatDate(it.UpdatedAt),
                        Author = it.CreatedBy,
                        HasUnpublishedChanges = it.HasUnpublishedChanges()
                    });
                }
            }
            catch (SpecialtyForbiddenException)
            {
                // El filtro explícito ?specialty= pidió algo fuera del scope del usuario.
                model.Items.Clear();
                model.Total = 0;
                Response.StatusCode = 403;
                ViewBag.DataError = "No tenés acceso a esa especialidad.";
            }
            catch (Exception ex)
            {
                Helpers.HandledErrors.Log(this, ex);
                model.Items.Clear();
                model.Total = 0;
                ViewBag.DataError = "Ocurrió un error al procesar la solicitud.";
            }
            return View(model);
        }

        // Q2 — Editor (host Vue). id null = nuevo cuestionario.
        //
        // El editor es una pantalla de escritura y nada más: sin questionnaire.write no se
        // entra, ni siquiera en modo lectura. Para ver qué hay adentro de un cuestionario
        // está Preview, que es la vista de lectura de verdad, y es a donde apuntan los links
        // del listado para un Medico; el 403 del filtro queda para el acceso directo por URL.
        [RequiresPermission(Permissions.QuestionnaireWrite)]
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
            var title = "";
            var specialty = "";
            // Número de la versión PUBLICADA (0 = nunca se publicó), no el de la próxima:
            // el editor tiene que decir cuál es la que están usando los clínicos.
            var publishedVersion = 0;
            var status = "draft";
            var pendingChanges = false;
            var concurrencyVersion = 0;
            var groups = new List<QuestionGroupView>();
            // Codificación clínica del cuestionario completo (opcional). Mismo contrato que la
            // de cada pregunta: se lo desarma en la capa de negocio y acá sólo se pasa al editor.
            string code = null, codeDisplay = null, term = null;

            var cat = await _specialties.GetAsync();
            // Cuestionario nuevo: NO se preselecciona el default global acá. El default
            // correcto depende del scope del usuario (un Jefe restringido a "cardio" no
            // tiene por qué tener "general"), y el scope sólo lo conoce el fetch a
            // /api/specialties que hace el editor -- se deja vacío para que ese fetch
            // decida (ver loadSpecialties() en el editor).
            specialty = "";
            if (id.HasValue)
            {
                try
                {
                    var q = await _svc.GetAsync(id.Value);
                    title = q?.Title ?? "Cuestionario";
                    if (q != null)
                    {
                        publishedVersion = q.CurrentVersion()?.VersionNumber ?? 0;
                        specialty = cat.CodeOrDefault(q.SpecialtyCode);
                        status = QuestionnaireStatusMap.ToUi(q.Status);
                        pendingChanges = q.HasUnpublishedChanges();
                        concurrencyVersion = q.ConcurrencyVersion;
                        // El editor se hidrata con la estructura real desde el server; antes
                        // arrancaba con datos de ejemplo hardcodeados y pisaba lo guardado.
                        groups = BuildEditorGroups(q, await VariablesDe(q));

                        var coding = QuestionMetadataReader.ReadCoding(q.CodingJson);
                        if (coding != null)
                        {
                            code = coding.Code;
                            codeDisplay = coding.Display;
                            term = coding.Term;
                        }
                    }
                }
                catch (SpecialtyForbiddenException)
                {
                    return HttpNotFound();
                }
                catch (Exception ex)
                {
                    Helpers.HandledErrors.Log(this, ex);
                    title = null;
                    publishedVersion = 0;
                    ViewBag.DataError = "Ocurrió un error al procesar la solicitud.";
                }
            }

            // Con [RequiresPermission(QuestionnaireWrite)] arriba esto ya es siempre true, pero
            // se sigue calculando desde RolePolicy -no hardcodeado- para que el día que el
            // editor vuelva a abrirse en lectura para algún rol, los botones Guardar/Publicar
            // (Edit.cshtml) y las acciones internas del editor (prop canEdit, ver
            // questionnaire-editor/index.js) sigan saliendo de la MISMA fuente de verdad.
            var canEdit = RolePolicy.Allows(CurrentUser.Roles(User as System.Security.Claims.ClaimsPrincipal), Permissions.QuestionnaireWrite);

            ViewBag.QuestionnaireId = editorId;
            ViewBag.IsNew = isNew;
            ViewBag.QuestionnaireTitle = title;
            ViewBag.Version = publishedVersion;
            ViewBag.Status = status;
            ViewBag.HasUnpublishedChanges = pendingChanges;
            ViewBag.CanEdit = canEdit;
            // El JSON de props se arma acá (no en la vista) para no serializar dynamics.
            // status manda: el editor no autoguarda un cuestionario publicado.
            ViewBag.EditorProps = JsonConvert.SerializeObject(
                new { id = editorId, title, version = publishedVersion, isNew, specialtyCode = specialty, status, concurrencyVersion, groups, hasPendingChanges = pendingChanges, code, codeDisplay, term, canEdit },
                new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() });
            return View();
        }

        // Modelo interno del editor (secciones → preguntas) para la versión ACTUAL en base,
        // en el MISMO formato que props.groups. Lo consume el merge de conflictos del editor
        // para traer "la versión de la otra persona" y compararla, sin reparsear FHIR en el
        // cliente. Es de sólo lectura y reutiliza el mismo armado que hidrata el editor, pero
        // pide questionnaire.write igual que Edit: es superficie del editor, y quien no entra
        // al editor no tiene por qué poder consultarla (para leer un cuestionario está la API).
        [RequiresPermission(Permissions.QuestionnaireWrite)]
        public async Task<ActionResult> EditorModel(Guid id)
        {
            ChequeAR.Domain.Entities.Questionnaire q;
            try
            {
                q = await _svc.GetAsync(id);
            }
            catch (SpecialtyForbiddenException)
            {
                return HttpNotFound();
            }
            if (q == null) return HttpNotFound();
            var payload = new { concurrencyVersion = q.ConcurrencyVersion, groups = BuildEditorGroups(q, await VariablesDe(q)) };
            return Content(
                JsonConvert.SerializeObject(payload,
                    new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() }),
                "application/json");
        }

        // Candidatos para "importar preguntas" en el editor: cuestionarios de la MISMA
        // especialidad (no archivados, distinto del actual). Devuelve JSON liviano —sin las
        // preguntas, para no disparar N+1—; la estructura completa se trae recién al elegir uno,
        // reutilizando EditorModel. La lógica de filtrado vive en el service (testeable).
        [RequiresPermission(Permissions.QuestionnaireWrite)]
        public async Task<ActionResult> ImportCandidates(string specialty, Guid? excludeId = null)
        {
            try
            {
                var cat = await _specialties.GetAsync();
                var candidatos = await _svc.GetImportCandidatesAsync(specialty, excludeId);
                var payload = candidatos.Select(q => new
                {
                    id = q.Id,
                    title = q.Title,
                    status = QuestionnaireStatusMap.ToUi(q.Status),
                    specialtyLabel = cat.Label(q.SpecialtyCode, q.SpecialtyDisplay),
                    version = q.Versions?.Count ?? 0
                }).ToList();
                return Content(
                    JsonConvert.SerializeObject(payload,
                        new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() }),
                    "application/json");
            }
            catch (SpecialtyForbiddenException)
            {
                return new HttpStatusCodeResult(403, "No tenes acceso a esa especialidad.");
            }
            catch (Exception ex)
            {
                Helpers.HandledErrors.Log(this, ex);
                return new HttpStatusCodeResult(500, "Ocurrió un error al procesar la solicitud.");
            }
        }


        // Arma el árbol secciones -> preguntas que consume la isla Vue. Las preguntas sin
        // sección se juntan en una sección implícita para que sean editables igual.
        /// <summary>
        /// Trae de una sola vez las variables que usa el cuestionario, para que el editor pueda
        /// mostrar el nombre de cada una y detectar las desactualizadas. Una consulta por
        /// cuestionario y no una por pregunta: es el mismo criterio que el catálogo de
        /// especialidades en este mismo controller.
        /// </summary>
        private async Task<IDictionary<Guid, ChequeAR.Domain.Entities.ClinicalVariable>> VariablesDe(
            ChequeAR.Domain.Entities.Questionnaire q)
        {
            var ids = (q.Questions ?? new List<ChequeAR.Domain.Entities.Question>())
                .Where(x => x.VariableId.HasValue)
                .Select(x => x.VariableId.Value)
                .Distinct()
                .ToList();
            if (ids.Count == 0) return new Dictionary<Guid, ChequeAR.Domain.Entities.ClinicalVariable>();

            var encontradas = new Dictionary<Guid, ChequeAR.Domain.Entities.ClinicalVariable>();
            foreach (var id in ids)
            {
                var v = await _variables.GetAsync(id);
                if (v != null) encontradas[id] = v;
            }
            return encontradas;
        }

        private static List<QuestionGroupView> BuildEditorGroups(
            ChequeAR.Domain.Entities.Questionnaire q,
            IDictionary<Guid, ChequeAR.Domain.Entities.ClinicalVariable> variables = null)
        {
            var result = new List<QuestionGroupView>();
            var questions = q.Questions ?? new List<ChequeAR.Domain.Entities.Question>();
            var cat = variables ?? new Dictionary<Guid, ChequeAR.Domain.Entities.ClinicalVariable>();

            foreach (var g in (q.Groups ?? new List<ChequeAR.Domain.Entities.QuestionGroup>()).OrderBy(g => g.OrderIndex))
            {
                var view = new QuestionGroupView { Title = g.Title };
                foreach (var qq in questions.Where(x => x.GroupId == g.Id).OrderBy(x => x.OrderIndex))
                    view.Questions.Add(ToQuestionView(qq, cat));
                result.Add(view);
            }

            var loose = questions.Where(x => !x.GroupId.HasValue).OrderBy(x => x.OrderIndex).ToList();
            if (loose.Count > 0)
            {
                var view = new QuestionGroupView { Title = "Preguntas" };
                foreach (var qq in loose) view.Questions.Add(ToQuestionView(qq, cat));
                result.Add(view);
            }
            return result;
        }

        private static QuestionView ToQuestionView(
            ChequeAR.Domain.Entities.Question qq,
            IDictionary<Guid, ChequeAR.Domain.Entities.ClinicalVariable> variables = null)
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

            if (qq.VariableId.HasValue)
            {
                view.VariableId = qq.VariableId.Value.ToString();
                view.VariableRevision = qq.VariableRevision;
                ChequeAR.Domain.Entities.ClinicalVariable v = null;
                if (variables != null) variables.TryGetValue(qq.VariableId.Value, out v);
                // Si la variable ya no está en el catálogo, la pregunta sigue mostrándose con
                // su snapshot: lo que se perdió es poder nombrar el origen, no el contenido.
                view.VariableName = v != null ? v.Name : null;
                view.VariableCurrentRevision = v != null ? v.Revision : qq.VariableRevision;
            }
            return view;
        }


        // Q5 — Vista previa. Con version, muestra esa versión publicada tal como se congeló
        // (lo usa "Ver" en el Historial); sin version, el cuestionario actual.
        public async Task<ActionResult> Preview(Guid id, int? version = null)
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
                var cat = await _specialties.GetAsync();
                var model = new QuestionnaireDetailViewModel
                {
                    Id = q.Id, Title = q.Title,
                    SpecialtyCode = cat.CodeOrDefault(q.SpecialtyCode),
                    SpecialtyLabel = cat.Label(q.SpecialtyCode, q.SpecialtyDisplay),
                    SpecialtyColor = cat.Color(q.SpecialtyCode),
                    Version = q.Versions?.Count ?? 0,
                    Status = QuestionnaireStatusMap.ToUi(q.Status),
                    Author = q.CreatedBy,
                    UpdatedAt = ChequeAR.Api.Helpers.ArgentinaTime.FormatDate(q.UpdatedAt),
                    HasUnpublishedChanges = q.HasUnpublishedChanges()
                };

                if (version.HasValue)
                {
                    var ver = await _svc.GetVersionAsync(id, version.Value);
                    var snap = ver == null ? null : ChequeAR.Application.Fhir.FhirQuestionnaireReader.ReadDomain(ver.FhirJson);
                    if (snap == null) return HttpNotFound();

                    // Título, autor y fecha también de la versión: el título puede haber
                    // cambiado después, y lo que se audita es lo que vio el clínico.
                    model.SnapshotVersion = ver.VersionNumber;
                    model.Title = snap.Title ?? q.Title;
                    model.Author = ver.PublishedBy;
                    model.UpdatedAt = ChequeAR.Api.Helpers.ArgentinaTime.FormatDate(ver.PublishedAt);
                    model.Groups.AddRange(BuildEditorGroups(snap, await VariablesDe(snap)));
                    return View(model);
                }

                model.Groups.AddRange(BuildEditorGroups(q, await VariablesDe(q)));
                return View(model);
            }
            catch (SpecialtyForbiddenException)
            {
                return HttpNotFound();
            }
            catch (Exception ex)
            {
                Helpers.HandledErrors.Log(this, ex);
                ViewBag.DataError = "Ocurrió un error al procesar la solicitud.";
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
            catch (SpecialtyForbiddenException)
            {
                return HttpNotFound();
            }
            catch (Exception ex)
            {
                Helpers.HandledErrors.Log(this, ex);
                ViewBag.QuestionnaireTitle = null;
                ViewBag.DataError = "Ocurrió un error al procesar la solicitud.";
                return View(new List<QuestionnaireVersionView>());
            }
        }

        // Q9b — Comparar dos versiones publicadas. Sin parámetros compara la última contra
        // la anterior; desde el Historial llega con to = la fila y from = la previa.
        public async Task<ActionResult> Compare(Guid id, int? from = null, int? to = null)
        {
            ViewBag.Title = "Comparar versiones";
            ViewBag.PageId = "questionnaires";
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Cuestionarios", Url.Action("Index")),
                System.Tuple.Create("Historial", Url.Action("History", new { id })),
                System.Tuple.Create("Comparar", (string)null)
            };

            try
            {
                var q = await _svc.GetAsync(id);
                if (q == null) return HttpNotFound();

                var versiones = (q.Versions ?? new List<ChequeAR.Domain.Entities.QuestionnaireVersion>())
                    .OrderBy(v => v.VersionNumber).ToList();
                if (versiones.Count < 2)
                {
                    TempData["Toast"] = "Hace falta al menos dos versiones publicadas para comparar.";
                    TempData["ToastLevel"] = "error";
                    return RedirectToAction("History", new { id });
                }

                var hasta = to ?? versiones.Last().VersionNumber;
                var desde = from ?? versiones.Where(v => v.VersionNumber < hasta)
                                             .Select(v => (int?)v.VersionNumber).LastOrDefault()
                                   ?? versiones.First().VersionNumber;

                var vDesde = versiones.FirstOrDefault(v => v.VersionNumber == desde);
                var vHasta = versiones.FirstOrDefault(v => v.VersionNumber == hasta);
                if (vDesde == null || vHasta == null) return HttpNotFound();

                // Las dos salen del FHIR congelado, nunca de la entidad viva: una versión
                // publicada es inmutable y la comparación tiene que dar siempre lo mismo.
                var antes = ChequeAR.Application.Fhir.FhirQuestionnaireReader.ReadDomain(vDesde.FhirJson);
                var despues = ChequeAR.Application.Fhir.FhirQuestionnaireReader.ReadDomain(vHasta.FhirJson);

                var model = new QuestionnaireCompareViewModel
                {
                    Id = id,
                    Title = q.Title,
                    From = desde,
                    To = hasta,
                    FromDate = ChequeAR.Api.Helpers.ArgentinaTime.FormatDate(vDesde.PublishedAt),
                    ToDate = ChequeAR.Api.Helpers.ArgentinaTime.FormatDate(vHasta.PublishedAt),
                    FromAuthor = vDesde.PublishedBy,
                    ToAuthor = vHasta.PublishedBy,
                    ToChangeNote = vHasta.ChangeNote,
                    Versions = versiones.Select(v => v.VersionNumber).ToList(),
                    Diff = QuestionnaireVersionDiff.Compare(antes, despues)
                };
                return View(model);
            }
            catch (SpecialtyForbiddenException)
            {
                return HttpNotFound();
            }
        }

        [HttpPost, ValidateAntiForgeryToken, RequiresPermission(Permissions.QuestionnaireWrite)]
        public async Task<ActionResult> Publish(Guid id, string changeNote)
        {
            var userId = User?.Identity?.Name ?? "unknown";
            try
            {
                await _svc.PublishAsync(id, userId, changeNote);
                TempData["Toast"] = "Cuestionario publicado.";
                TempData["ToastLevel"] = "success";
            }
            catch (SpecialtyForbiddenException)
            {
                Response.StatusCode = 403;
                TempData["Toast"] = "No tenes acceso a la especialidad de ese cuestionario.";
                TempData["ToastLevel"] = "error";
            }
            catch (Exception ex)
            {
                Helpers.HandledErrors.Log(this, ex);
                TempData["Toast"] = "No se pudo publicar el cuestionario.";
                TempData["ToastLevel"] = "error";
            }
            return RedirectToAction("Index");
        }

        [HttpPost, ValidateAntiForgeryToken, RequiresPermission(Permissions.QuestionnaireWrite)]
        public async Task<ActionResult> Archive(Guid id)
        {
            var userId = User?.Identity?.Name ?? "unknown";
            try
            {
                await _svc.ArchiveAsync(id, userId);
                TempData["Toast"] = "Cuestionario archivado.";
                TempData["ToastLevel"] = "success";
            }
            catch (SpecialtyForbiddenException)
            {
                Response.StatusCode = 403;
                TempData["Toast"] = "No tenes acceso a la especialidad de ese cuestionario.";
                TempData["ToastLevel"] = "error";
            }
            catch (Exception ex)
            {
                Helpers.HandledErrors.Log(this, ex);
                TempData["Toast"] = "No se pudo archivar el cuestionario.";
                TempData["ToastLevel"] = "error";
            }
            return RedirectToAction("Index");
        }
    }
}
