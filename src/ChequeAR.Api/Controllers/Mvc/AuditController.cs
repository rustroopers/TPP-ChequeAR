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
        // Oracle no acepta más de 1000 elementos en un IN.
        private const int LookupChunk = 500;

        private readonly AuditQueryService _audit;
        private readonly QuestionnaireService _questionnaires;
        private readonly ClinicalVariableService _variables;
        private readonly QuestionnaireResponseService _responses;
        private readonly AccessAuditService _access;

        public AuditController(AuditQueryService audit, QuestionnaireService questionnaires,
            ClinicalVariableService variables, QuestionnaireResponseService responses, AccessAuditService access)
        {
            _audit = audit;
            _questionnaires = questionnaires;
            _variables = variables;
            _responses = responses;
            _access = access;
        }

        // A6. OJO: el parámetro NO puede llamarse "action" — colisiona con el token de ruta
        // {action} de MVC (nombre del método), que gana sobre el query string y rompía el filtro.
        public async Task<ActionResult> Index(string user = null, string act = null, string type = null, string from = null, string to = null, int page = 1)
        {
            ArgentinaTime.NormalizeFilterRange(ref from, ref to);
            ViewBag.Title = "Auditoría";
            ViewBag.PageId = "audit";
            const int pageSize = 50;
            var model = new AuditListViewModel
            {
                UserFilter = user, ActionFilter = act, ResourceTypeFilter = type,
                From = from, To = to, PageSize = pageSize,
                // Acotada para que (página - 1) * tamaño no desborde.
                Page = Math.Min(Math.Max(1, page), int.MaxValue / pageSize)
            };
            // Una exportación rechazada vuelve acá con el motivo (ver Export).
            ViewBag.ExportError = TempData["AuditExportError"] as string;

            try
            {
                var criteria = BuildCriteria(user, act, type, from, to);
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
        private static AuditSearchCriteria BuildCriteria(string user, string act, string type, string from, string to)
        {
            return new AuditSearchCriteria
            {
                AgentUserId = user,
                Action = act,
                ResourceType = string.IsNullOrWhiteSpace(type) ? null : type,
                FromUtc = ArgentinaTime.ParseFilterDateToUtc(from, endOfDay: false),
                ToUtc = ArgentinaTime.ParseFilterDateToUtc(to, endOfDay: true)
            };
        }

        /// <summary>Eventos ya traídos de la base, con forma de pantalla.</summary>
        private async Task<List<AuditEntry>> ToEntriesAsync(IReadOnlyList<AuditEvent> items)
        {
            var names = await ResolveNamesAsync(items);
            var entries = new List<AuditEntry>();
            foreach (var e in items)
            {
                var av = AuditPresentation.Describe(e.EntityRef, e.Action);
                entries.Add(new AuditEntry
                {
                    Id = e.Id,
                    Timestamp = ArgentinaTime.FormatDateTime(e.Recorded),
                    Username = e.AgentUserId,
                    Action = av.Label,
                    ActionBadge = av.BadgeClass,
                    ActionIcon = av.Icon,
                    Resource = AuditPresentation.ResourceTypeLabel(av.ResourceType),
                    ResourceName = ResourceName(av, e, names)
                });
            }
            return entries;
        }



        // A6b — Exportación. Reutiliza los mismos exporters (XLSX/CSV) que Reportes,
        // armando un ReportResult con las columnas visibles del listado.
        public async Task<ActionResult> Export(string user = null, string act = null, string type = null, string from = null, string to = null, string format = "xlsx")
        {
            ArgentinaTime.NormalizeFilterRange(ref from, ref to);
            // Exporta exactamente lo que muestra el listado con los filtros aplicados (sin paginar).
            List<AuditEntry> entries;
            try
            {
                var criteria = BuildCriteria(user, act, type, from, to);
                entries = await ToEntriesAsync(await _audit.ExportAsync(criteria));
            }
            catch (ChequeAR.Application.Exceptions.BusinessRuleException ex)
            {
                // Demasiados eventos: se vuelve al listado con el motivo, no se baja un archivo recortado.
                TempData["AuditExportError"] = ex.Message;
                return RedirectToAction("Index", new { user, act, type, from, to });
            }
            // Bajar el registro de auditoría también es un acceso que tiene que quedar registrado.
            _access.AuditExported(new AuditExportSnapshot
            {
                User = user, Action = act,
                ResourceType = string.IsNullOrWhiteSpace(type) ? null : AuditPresentation.ResourceTypeLabel(type),
                From = from, To = to, Format = format
            }, entries.Count);
            var rows = entries.ConvertAll(e => new Dictionary<string, object>
            {
                ["Fecha"] = e.Timestamp,
                ["Usuario"] = e.Username,
                ["Acción"] = e.Action,
                ["Recurso"] = e.Resource,
                ["Nombre"] = e.ResourceName
            });

            var result = new ReportResult { Total = rows.Count, Rows = rows };
            var dateSuffix = ArgentinaTime.From(DateTime.UtcNow).ToString("yyyy-MM-dd");
            if ((format ?? "xlsx").ToLowerInvariant() == "csv")
                return File(CsvExporter.Export(result), "text/csv", $"auditoria_{dateSuffix}.csv");
            return File(XlsxExporter.Export(result),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"auditoria_{dateSuffix}.xlsx");
        }

        // A7
        public async Task<ActionResult> Details(Guid id)
        {
            ViewBag.Title = "Evento de auditoría";
            ViewBag.PageId = "audit";
            try
            {
                var e = await _audit.GetAsync(id);
                if (e == null) return HttpNotFound();

                var av = AuditPresentation.Describe(e.EntityRef, e.Action);
                var names = await ResolveNamesAsync(new List<AuditEvent> { e });

                var detail = new AuditDetailViewModel
                {
                    Id = e.Id,
                    Timestamp = ArgentinaTime.FormatDateTime(e.Recorded),
                    Username = e.AgentUserId,
                    Action = av.Label,
                    ActionBadge = av.BadgeClass,
                    ActionIcon = av.Icon,
                    Resource = AuditPresentation.ResourceTypeLabel(av.ResourceType),
                    ResourceName = ResourceName(av, e, names)
                };

                // Para una publicación mostramos de qué versión a qué versión pasó y el diff.
                if (av.Operation == AuditPresentation.Op.Publish && av.ResourceType == "Questionnaire" && av.ResourceId.HasValue)
                    await FillVersionDiffAsync(detail, av.ResourceId.Value, e.Recorded);

                // Para una consulta guardada mostramos su nombre, alcance y condiciones; para un
                // reporte exportado, las condiciones con las que se corrió.
                if (av.ResourceType == "SavedReport" || av.ResourceType == "Report")
                    await FillReportDetailAsync(detail, e.FhirJson, saved: av.ResourceType == "SavedReport");
                else if (av.ResourceType == "Patient" && e.EntityRef.Contains("$trend"))
                    await FillTrendDetailAsync(detail, e.FhirJson);
                else if (av.ResourceType == "Patient" && e.EntityRef.Contains("$search"))
                    FillPatientSearchDetail(detail, e.FhirJson);
                else if (av.ResourceType == "AuditEvent")
                    FillAuditExportDetail(detail, e.FhirJson);
                else if (av.ResourceType == "AppUser"
                         || (e.Action == "U" && (av.ResourceType == "ClinicalVariable" || av.ResourceType == "Questionnaire") && !e.EntityRef.Contains("$")))
                    FillChanges(detail, AuditChangeSet.Read(e.FhirJson));

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

            var afterLines = QuestionnaireReadableLines.From(to.FhirJson);
            var beforeLines = from != null
                ? QuestionnaireReadableLines.From(from.FhirJson)
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

            if (snap.Changes != null) detail.FieldChanges = snap.Changes;
            if (snap.PreviousQuestionnaireId.HasValue)
            {
                var anterior = await _questionnaires.GetAsync(snap.PreviousQuestionnaireId.Value);
                detail.FieldChanges.Add(new AuditFieldChange { Field = "Cuestionario", Before = anterior?.Title, After = detail.ReportQuestionnaire });
                // Las condiciones anteriores hablan de las preguntas del cuestionario anterior.
                if (snap.PreviousQueryJson != null && anterior?.Questions != null)
                    linkTexts = anterior.Questions.Where(qq => !string.IsNullOrEmpty(qq.LinkId))
                        .GroupBy(qq => qq.LinkId).ToDictionary(g => g.Key, g => g.First().Text);
            }
            if (snap.PreviousQueryJson != null)
                detail.PreviousReportConditions = AuditPresentation.DescribeReportConditions(snap.PreviousQueryJson, linkTexts);
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
            if (!string.IsNullOrWhiteSpace(snap.ResourceType)) detail.ReportConditions.Add("Recurso: " + snap.ResourceType);
            if (!string.IsNullOrWhiteSpace(snap.Questionnaire)) detail.ReportConditions.Add("Cuestionario: " + snap.Questionnaire);
            if (!string.IsNullOrWhiteSpace(snap.From) || !string.IsNullOrWhiteSpace(snap.To))
                detail.ReportConditions.Add("Período: " + (string.IsNullOrWhiteSpace(snap.From) ? "el inicio" : snap.From)
                                            + " a " + (string.IsNullOrWhiteSpace(snap.To) ? "hoy" : snap.To));
        }

        private static void FillPatientSearchDetail(AuditDetailViewModel detail, string fhirJson)
        {
            PatientSearchAuditSnapshot snap;
            try { snap = Newtonsoft.Json.JsonConvert.DeserializeObject<PatientSearchAuditSnapshot>(fhirJson ?? string.Empty); }
            catch (Newtonsoft.Json.JsonException) { return; }
            if (snap == null) return;
            detail.HasReportDetail = true;
            detail.ReportHeading = "Búsqueda";
            detail.ConditionsHeading = "Datos";
            detail.ReportConditions.Add("Resultados: " + snap.Results);
            detail.ReportConditions.Add("Término (SHA-256): " + snap.TermSha256);
        }

        /// <summary>Qué cambió en una edición. Los eventos anteriores a que se guardara no traen nada.</summary>
        private static void FillChanges(AuditDetailViewModel detail, AuditChangeSet changes)
        {
            if (changes == null) return;
            detail.FieldChanges = changes.Changes ?? new List<AuditFieldChange>();
            detail.RemovedLines = changes.Removed ?? new List<string>();
            detail.AddedLines = changes.Added ?? new List<string>();
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
                case "E": return "Exportaciones e ingresos";
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

        /// <summary>Nombres de cuestionarios, variables y fichas de los eventos, en pocas consultas.</summary>
        private sealed class ResourceNames
        {
            public Dictionary<Guid, string> Questionnaires { get; } = new Dictionary<Guid, string>();
            public Dictionary<Guid, string> Variables { get; } = new Dictionary<Guid, string>();
            public Dictionary<Guid, QuestionnaireResponse> Responses { get; } = new Dictionary<Guid, QuestionnaireResponse>();
        }

        private async Task<ResourceNames> ResolveNamesAsync(IEnumerable<AuditEvent> events)
        {
            var questionnaireIds = new HashSet<Guid>();
            var variableIds = new HashSet<Guid>();
            var responseIds = new HashSet<Guid>();
            foreach (var e in events)
            {
                AuditPresentation.ParseRef(e.EntityRef, out var type, out var eid, out _);
                // Un reporte se identifica por el cuestionario sobre el que corrió.
                var deReporte = AuditPresentation.ReportQuestionnaireId(e.EntityRef);
                if (deReporte.HasValue) questionnaireIds.Add(deReporte.Value);
                if (!eid.HasValue) continue;
                if (type == "Questionnaire") questionnaireIds.Add(eid.Value);
                else if (type == "ClinicalVariable") variableIds.Add(eid.Value);
                else if (type == "QuestionnaireResponse") responseIds.Add(eid.Value);
            }

            var names = new ResourceNames();
            foreach (var chunk in Chunks(responseIds))
                foreach (var r in await _responses.GetHeadersForAuditAsync(chunk))
                {
                    names.Responses[r.Id] = r;
                    questionnaireIds.Add(r.QuestionnaireId);
                }
            foreach (var chunk in Chunks(variableIds))
                foreach (var v in await _variables.GetManyAsync(chunk))
                    names.Variables[v.Id] = v.Name;
            foreach (var chunk in Chunks(questionnaireIds))
                foreach (var t in await _questionnaires.GetTitlesAsync(chunk))
                    names.Questionnaires[t.Key] = t.Value;
            return names;
        }

        private static IEnumerable<List<Guid>> Chunks(IEnumerable<Guid> ids)
        {
            var list = ids.ToList();
            for (var i = 0; i < list.Count; i += LookupChunk)
                yield return list.GetRange(i, Math.Min(LookupChunk, list.Count - i));
        }

        /// <summary>Nombre del recurso puntual del evento; "—" si no tiene o ya no existe.</summary>
        private static string ResourceName(AuditPresentation.ActionView av, AuditEvent e, ResourceNames names)
        {
            var entityRef = e.EntityRef ?? string.Empty;
            var id = av.ResourceId;
            string nombre = null;
            switch (av.ResourceType)
            {
                case "Questionnaire":
                    if (id.HasValue) names.Questionnaires.TryGetValue(id.Value, out nombre);
                    break;
                case "ClinicalVariable":
                    if (id.HasValue) names.Variables.TryGetValue(id.Value, out nombre);
                    break;
                case "QuestionnaireResponse":
                    if (id.HasValue && names.Responses.TryGetValue(id.Value, out var r))
                    {
                        names.Questionnaires.TryGetValue(r.QuestionnaireId, out var titulo);
                        var hc = string.IsNullOrWhiteSpace(r.PatientHospitalId) ? null : "HC " + r.PatientHospitalId;
                        nombre = string.Join(" · ", new[] { titulo, hc }.Where(x => !string.IsNullOrWhiteSpace(x)));
                    }
                    break;
                case "Report":
                    var deReporte = AuditPresentation.ReportQuestionnaireId(entityRef);
                    if (deReporte.HasValue) names.Questionnaires.TryGetValue(deReporte.Value, out nombre);
                    break;
                case "SavedReport":
                    nombre = AuditPresentation.ReadSavedReport(e.FhirJson)?.Name;
                    break;
                case "Patient":
                    // La búsqueda no apunta a un paciente: el término se guarda sólo como hash.
                    if (!entityRef.StartsWith("Patient$", StringComparison.Ordinal))
                        nombre = "HC " + AuditPresentation.PatientHospitalId(entityRef);
                    break;
                case "AppUser":
                    nombre = AuditPresentation.UserId(entityRef);
                    break;
                case "Http":
                    nombre = "/" + entityRef.Substring("Http/".Length);
                    break;
            }
            return string.IsNullOrWhiteSpace(nombre) ? "—" : nombre;
        }
    }
}
