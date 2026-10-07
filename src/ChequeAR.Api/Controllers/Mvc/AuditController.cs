using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using ChequeAR.Api.Auth;
using ChequeAR.Api.Helpers;
using ChequeAR.Api.Models;
using ChequeAR.Application.Exporters;
using ChequeAR.Application.Services;
using ChequeAR.Application.Reports;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Api.Controllers.Mvc
{
    // Saca a Jefe (tenía acceso antes): auditoría queda exclusiva de Administrador.
    [RequiresPermission(Permissions.AuditRead)]
    public class AuditController : Controller
    {
        private readonly AuditQueryService _audit;
        private readonly QuestionnaireService _questionnaires;
        private readonly AccessAuditService _access;

        public AuditController(AuditQueryService audit, QuestionnaireService questionnaires, AccessAuditService access)
        {
            _audit = audit;
            _questionnaires = questionnaires;
            _access = access;
        }

        // A6. OJO: el parámetro NO puede llamarse "action" — colisiona con el token de ruta
        // {action} de MVC (nombre del método), que gana sobre el query string y rompía el filtro.
        public async Task<ActionResult> Index(string user = null, string act = null, Guid? questionnaire = null, string from = null, string to = null, int page = 1)
        {
            ArgentinaTime.NormalizeFilterRange(ref from, ref to);
            ViewBag.Title = "Auditoría";
            ViewBag.PageId = "audit";
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Auditoría", (string)null)
            };
            const int pageSize = 50;
            var model = new AuditListViewModel
            {
                UserFilter = user, ActionFilter = act, QuestionnaireFilter = questionnaire,
                From = from, To = to, PageSize = pageSize,
                // Acotada para que (página - 1) * tamaño no desborde.
                Page = Math.Min(Math.Max(1, page), int.MaxValue / pageSize)
            };
            // Una exportación rechazada vuelve acá con el motivo (ver Export).
            ViewBag.ExportError = TempData["AuditExportError"] as string;

            try
            {
                model.Questionnaires.AddRange(await _questionnaires.ListTitlesAsync());
                var criteria = BuildCriteria(user, act, questionnaire, from, to);
                var pagina = await _audit.SearchAsync(criteria, (model.Page - 1) * pageSize, pageSize);
                model.Total = pagina.Total;
                model.Entries.AddRange(await ToEntriesAsync(pagina.Items));
            }
            catch (Exception ex)
            {
                Helpers.HandledErrors.Log(this, ex);
                model.Entries.Clear();
                model.Total = 0;
                ViewBag.DataError = "Ocurrió un error al procesar la solicitud.";
            }
            return View(model);
        }

        /// <summary>Filtros del listado, tal como los entiende el servicio.</summary>
        private static AuditSearchCriteria BuildCriteria(string user, string act, Guid? questionnaire, string from, string to)
        {
            return new AuditSearchCriteria
            {
                AgentUserId = user,
                Action = act,
                QuestionnaireId = questionnaire,
                FromUtc = ArgentinaTime.ParseFilterDateToUtc(from, endOfDay: false),
                ToUtc = ArgentinaTime.ParseFilterDateToUtc(to, endOfDay: true)
            };
        }

        /// <summary>Eventos ya traídos de la base, con forma de pantalla.</summary>
        private async Task<List<AuditEntry>> ToEntriesAsync(IReadOnlyList<AuditEvent> items)
        {
            var titles = await ResolveTitlesAsync(items);
            var entries = new List<AuditEntry>();
            foreach (var e in items)
            {
                var av = AuditPresentation.Describe(e.EntityRef, e.Action);
                var label = ResourceLabel(av, titles, e.FhirJson, e.EntityRef);
                entries.Add(new AuditEntry
                {
                    Id = e.Id,
                    Timestamp = ArgentinaTime.FormatDateTime(e.Recorded),
                    Username = e.AgentUserId,
                    Action = av.Label,
                    ActionBadge = av.BadgeClass,
                    ActionIcon = av.Icon,
                    Resource = label
                });
            }
            return entries;
        }



        // A6b — Exportación. Reutiliza los mismos exporters (XLSX/CSV) que Reportes,
        // armando un ReportResult con las columnas visibles del listado.
        public async Task<ActionResult> Export(string user = null, string act = null, Guid? questionnaire = null, string from = null, string to = null, string format = "xlsx")
        {
            ArgentinaTime.NormalizeFilterRange(ref from, ref to);
            // Exporta exactamente lo que muestra el listado con los filtros aplicados (sin paginar).
            List<AuditEntry> entries;
            try
            {
                var criteria = BuildCriteria(user, act, questionnaire, from, to);
                entries = await ToEntriesAsync(await _audit.ExportAsync(criteria));
            }
            catch (ChequeAR.Application.Exceptions.BusinessRuleException ex)
            {
                // Demasiados eventos: se vuelve al listado con el motivo, no se baja un archivo recortado.
                TempData["AuditExportError"] = ex.Message;
                return RedirectToAction("Index", new { user, act, questionnaire, from, to });
            }
            // Bajar el registro de auditoría también es un acceso que tiene que quedar registrado.
            string titulo = null;
            if (questionnaire.HasValue)
            {
                var titulos = await _questionnaires.GetTitlesAsync(new[] { questionnaire.Value });
                titulo = titulos.TryGetValue(questionnaire.Value, out var t) ? t : questionnaire.Value.ToString();
            }
            _access.AuditExported(new AuditExportSnapshot
            {
                User = user, Action = act, Questionnaire = titulo, From = from, To = to, Format = format
            }, entries.Count);
            var rows = entries.ConvertAll(e => new Dictionary<string, object>
            {
                ["Fecha"] = e.Timestamp,
                ["Usuario"] = e.Username,
                ["Acción"] = e.Action,
                ["Cuestionario"] = e.Resource
            });

            var result = new ReportResult { Total = rows.Count, Rows = rows };
            var dateSuffix = DateTime.Now.ToString("yyyy-MM-dd");
            if ((format ?? "xlsx").ToLowerInvariant() == "csv")
                return File(CsvExporter.Export(result), "text/csv", $"auditoria_{dateSuffix}.csv");
            return File(XlsxExporter.Export(result),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"auditoria_{dateSuffix}.xlsx");
        }

        // A7
        public async Task<ActionResult> Details(Guid id)
        {
            ViewBag.Title = "Evento #" + id;
            ViewBag.PageId = "audit";
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Auditoría", Url.Action("Index")),
                System.Tuple.Create("#" + id.ToString().Substring(0, 8), (string)null)
            };
            try
            {
                var e = await _audit.GetAsync(id);
                if (e == null) return HttpNotFound();

                var av = AuditPresentation.Describe(e.EntityRef, e.Action);
                var titles = await ResolveTitlesAsync(new List<ChequeAR.Domain.Entities.AuditEvent> { e });

                var detail = new AuditDetailViewModel
                {
                    Id = e.Id,
                    Timestamp = ArgentinaTime.FormatDateTime(e.Recorded),
                    Username = e.AgentUserId,
                    Action = av.Label,
                    ActionBadge = av.BadgeClass,
                    ActionIcon = av.Icon,
                    Resource = ResourceLabel(av, titles, e.FhirJson, e.EntityRef)
                };

                // Para una publicación mostramos de qué versión a qué versión pasó y el diff.
                if (av.Operation == AuditPresentation.Op.Publish && av.ResourceId.HasValue)
                    await FillVersionDiffAsync(detail, av.ResourceId.Value, e.Recorded);

                // Para una consulta guardada mostramos su nombre, alcance y condiciones; para un
                // reporte consultado o exportado, las condiciones con las que se corrió.
                if (av.ResourceType == "SavedReport" || av.ResourceType == "Report")
                    await FillReportDetailAsync(detail, e.FhirJson, saved: av.ResourceType == "SavedReport");
                else if (av.ResourceType == "Patient")
                    await FillTrendDetailAsync(detail, e.FhirJson);
                else if (av.ResourceType == "AuditEvent")
                    FillAuditExportDetail(detail, e.FhirJson);

                // Para una ficha: al finalizar se guardó la foto COMPLETA (todas las
                // respuestas); en un guardado de avance, sólo el diff de esa sesión.
                if (av.ResourceType == "QuestionnaireResponse" && !string.IsNullOrWhiteSpace(e.FhirJson))
                {
                    if (av.Operation == AuditPresentation.Op.Finalize)
                        FillResponseSnapshot(detail, e.FhirJson);
                    else
                        FillResponseDiff(detail, e.FhirJson);
                }

                return View(detail);
            }
            catch (Exception ex)
            {
                Helpers.HandledErrors.Log(this, ex);
                ViewBag.DataError = "Ocurrió un error al procesar la solicitud.";
                return View(new AuditDetailViewModel { Id = id });
            }
        }

        /// <summary>
        /// Identifica la versión que quedó congelada en esta publicación (la publicada en el
        /// mismo instante del evento) y arma el diff contra la inmediatamente anterior. No
        /// mira las versiones posteriores: este registro solo refleja ESA transición.
        /// </summary>
        private async Task FillVersionDiffAsync(AuditDetailViewModel detail, Guid questionnaireId, DateTime recorded)
        {
            var q = await _questionnaires.GetAsync(questionnaireId);
            var versions = (q?.Versions ?? new List<QuestionnaireVersion>())
                .OrderBy(v => v.VersionNumber).ToList();
            if (versions.Count == 0) return;

            // La versión de este evento es la publicada más cerca (en tiempo) del registro.
            var to = versions.OrderBy(v => Math.Abs((v.PublishedAt - recorded).Ticks)).First();
            var from = versions.FirstOrDefault(v => v.VersionNumber == to.VersionNumber - 1);

            detail.HasVersionDiff = true;
            detail.ToVersion = to.VersionNumber;
            detail.FromVersion = from?.VersionNumber;

            var afterLines = AuditPresentation.FlattenForHumans(to.FhirJson);
            var beforeLines = from != null
                ? AuditPresentation.FlattenForHumans(from.FhirJson)
                : new List<string>();

            AuditPresentation.Diff(beforeLines, afterLines,
                out var beforeMarked, out var afterMarked);
            detail.BeforeLines = beforeMarked;
            detail.AfterLines = afterMarked;
        }

        /// <summary>
        /// Lee el diff de una sesión de guardado (lista de <see cref="Application.Services.ResponseFieldChange"/>
        /// serializada en el evento) y lo carga en el detalle. Si el JSON no es interpretable
        /// se ignora en silencio: es un extra de presentación, no un dato crítico.
        /// </summary>
        private static void FillResponseDiff(AuditDetailViewModel detail, string fhirJson)
        {
            try
            {
                var changes = Newtonsoft.Json.JsonConvert
                    .DeserializeObject<List<Application.Services.ResponseFieldChange>>(fhirJson);
                if (changes == null || changes.Count == 0) return;
                detail.HasResponseDiff = true;
                detail.ResponseChanges = changes;
            }
            catch (Newtonsoft.Json.JsonException) { /* payload viejo o ilegible: sin diff */ }
        }

        /// <summary>
        /// Lee la foto completa de una ficha finalizada (lista de
        /// <see cref="Application.Services.ResponseSnapshotSection"/> serializada) y la carga en
        /// el detalle. Si el JSON no es interpretable se ignora en silencio.
        /// </summary>
        private static void FillResponseSnapshot(AuditDetailViewModel detail, string fhirJson)
        {
            try
            {
                var sections = Newtonsoft.Json.JsonConvert
                    .DeserializeObject<List<Application.Services.ResponseSnapshotSection>>(fhirJson);
                if (sections == null || sections.Count == 0) return;
                detail.HasResponseSnapshot = true;
                detail.ResponseSnapshot = sections;
            }
            catch (Newtonsoft.Json.JsonException) { /* payload viejo o ilegible: sin foto */ }
        }

        /// <summary>
        /// Carga el detalle de una consulta guardada desde la foto del evento: nombre, alcance,
        /// cuestionario base y las condiciones ya traducidas a texto legible (resolviendo cada
        /// linkId contra las preguntas del cuestionario). Si la foto no es interpretable, no
        /// agrega nada: es un extra de presentación, no un dato crítico.
        /// </summary>
        private async Task FillReportDetailAsync(AuditDetailViewModel detail, string fhirJson, bool saved)
        {
            var snap = AuditPresentation.ReadSavedReport(fhirJson);
            if (snap == null) return;

            detail.HasReportDetail = true;
            // Nombre y alcance son del reporte guardado; una consulta suelta no los tiene.
            if (saved)
            {
                detail.ReportName = snap.Name;
                detail.ReportScope = ScopeLabel(snap.Scope, snap.SpecialtyCode);
            }

            IReadOnlyDictionary<string, string> linkTexts = new Dictionary<string, string>();
            if (snap.QuestionnaireId != Guid.Empty)
            {
                var q = await _questionnaires.GetAsync(snap.QuestionnaireId);
                if (q != null)
                {
                    detail.ReportQuestionnaire = q.Title;
                    if (q.Questions != null)
                    {
                        var map = new Dictionary<string, string>();
                        foreach (var qq in q.Questions)
                            if (!string.IsNullOrEmpty(qq.LinkId) && !map.ContainsKey(qq.LinkId))
                                map[qq.LinkId] = qq.Text;
                        linkTexts = map;
                    }
                }
            }

            detail.ReportConditions = AuditPresentation.DescribeReportConditions(snap.QueryJson, linkTexts);
        }

        /// <summary>
        /// Detalle de una consulta de evolución: paciente, cuestionario y parámetros mirados.
        /// Si la foto no es interpretable no agrega nada.
        /// </summary>
        private async Task FillTrendDetailAsync(AuditDetailViewModel detail, string fhirJson)
        {
            TrendAuditSnapshot snap;
            try { snap = Newtonsoft.Json.JsonConvert.DeserializeObject<TrendAuditSnapshot>(fhirJson ?? string.Empty); }
            catch (Newtonsoft.Json.JsonException) { return; }
            if (snap == null) return;

            detail.HasReportDetail = true;
            detail.ReportHeading = "Evolución";
            detail.ConditionsHeading = "Consulta";
            detail.ReportConditions.Add("Paciente: HC " + snap.PatientHospitalId);

            var textos = new Dictionary<string, string>();
            var q = await _questionnaires.GetAsync(snap.QuestionnaireId);
            if (q != null)
            {
                detail.ReportQuestionnaire = q.Title;
                foreach (var qq in q.Questions ?? new List<Question>())
                    if (!string.IsNullOrEmpty(qq.LinkId) && !textos.ContainsKey(qq.LinkId))
                        textos[qq.LinkId] = qq.Text;
            }
            var parametros = (snap.LinkIds ?? new List<string>())
                .Select(l => textos.TryGetValue(l, out var t) && !string.IsNullOrWhiteSpace(t) ? t : l);
            detail.ReportConditions.Add("Parámetros: " + string.Join(", ", parametros));
        }

        /// <summary>Detalle de una exportación de la auditoría: los filtros con los que se bajó.</summary>
        private static void FillAuditExportDetail(AuditDetailViewModel detail, string fhirJson)
        {
            AuditExportSnapshot snap;
            try { snap = Newtonsoft.Json.JsonConvert.DeserializeObject<AuditExportSnapshot>(fhirJson ?? string.Empty); }
            catch (Newtonsoft.Json.JsonException) { return; }
            if (snap == null) return;

            detail.HasReportDetail = true;
            detail.ReportHeading = "Exportación";
            detail.ConditionsHeading = "Filtros";
            detail.NoConditionsText = "Sin filtros: incluye todos los eventos.";
            if (!string.IsNullOrWhiteSpace(snap.User)) detail.ReportConditions.Add("Usuario: " + snap.User);
            if (!string.IsNullOrWhiteSpace(snap.Action)) detail.ReportConditions.Add("Acción: " + ActionFilterLabel(snap.Action));
            if (!string.IsNullOrWhiteSpace(snap.Questionnaire)) detail.ReportConditions.Add("Cuestionario: " + snap.Questionnaire);
            if (!string.IsNullOrWhiteSpace(snap.From) || !string.IsNullOrWhiteSpace(snap.To))
                detail.ReportConditions.Add("Período: " + (string.IsNullOrWhiteSpace(snap.From) ? "el inicio" : snap.From)
                                            + " a " + (string.IsNullOrWhiteSpace(snap.To) ? "hoy" : snap.To));
        }

        /// <summary>Mismas etiquetas que el selector "Acción" del listado.</summary>
        private static string ActionFilterLabel(string code)
        {
            switch (code)
            {
                case "R": return "Consultas";
                case "C": return "Creaciones";
                case "U": return "Modificaciones";
                case "D": return "Eliminaciones";
                case "E": return "Exportaciones";
                default: return code;
            }
        }

        /// <summary>Alcance legible: privada, o compartida con una especialidad.</summary>
        private static string ScopeLabel(string scope, string specialtyCode)
        {
            if (string.Equals(scope, "specialty", StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrWhiteSpace(specialtyCode)
                    ? "Compartida con la especialidad"
                    : "Compartida con la especialidad " + specialtyCode;
            return "Privada (sólo el autor)";
        }

        private async Task<IReadOnlyDictionary<Guid, string>> ResolveTitlesAsync(IEnumerable<AuditEvent> events)
        {
            var ids = new List<Guid>();
            foreach (var e in events)
            {
                AuditPresentation.ParseRef(e.EntityRef, out var type, out var eid, out _);
                if (type == "Questionnaire" && eid.HasValue) ids.Add(eid.Value);
                // Un reporte se identifica por el cuestionario sobre el que corrió.
                var deReporte = AuditPresentation.ReportQuestionnaireId(e.EntityRef);
                if (deReporte.HasValue) ids.Add(deReporte.Value);
            }
            if (ids.Count == 0) return new Dictionary<Guid, string>();
            return await _questionnaires.GetTitlesAsync(ids);
        }

        /// <summary>Recurso legible: título del cuestionario si lo hay; si no, una ref limpia.</summary>
        private static string ResourceLabel(AuditPresentation.ActionView av, IReadOnlyDictionary<Guid, string> titles, string fhirJson = null, string entityRef = null)
        {
            var deReporte = AuditPresentation.ReportQuestionnaireId(entityRef);
            if (deReporte.HasValue && titles.TryGetValue(deReporte.Value, out var tituloReporte)
                && !string.IsNullOrWhiteSpace(tituloReporte))
                return tituloReporte;
            if (av.ResourceType == "Patient")
                return "Paciente HC " + AuditPresentation.PatientHospitalId(entityRef);
            if (av.ResourceType == "AuditEvent") return "Registro de auditoría";

            if (av.ResourceType == "Questionnaire" && av.ResourceId.HasValue
                && titles.TryGetValue(av.ResourceId.Value, out var title)
                && !string.IsNullOrWhiteSpace(title))
                return title;

            if (av.ResourceType == "SavedReport")
            {
                var snap = AuditPresentation.ReadSavedReport(fhirJson);
                return !string.IsNullOrWhiteSpace(snap?.Name) ? snap.Name : "Reporte guardado";
            }
            if (av.ResourceType == "QuestionnaireResponse") return "Respuesta de cuestionario";
            if (av.ResourceType == "Report") return "Reporte";
            if (!string.IsNullOrWhiteSpace(av.ResourceType)) return av.ResourceType;
            return "—";
        }
    }
}
