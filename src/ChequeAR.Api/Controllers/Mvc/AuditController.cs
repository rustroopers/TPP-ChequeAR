using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using ChequeAR.Api.Helpers;
using ChequeAR.Api.Models;
using ChequeAR.Application.Exporters;
using ChequeAR.Application.Services;
using ChequeAR.Application.Reports;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Api.Controllers.Mvc
{
    // TODO: re-habilitar [Authorize(Roles = Role.Administrador + "," + Role.Jefe)] cuando FormsAuth esté wired.
    public class AuditController : Controller
    {
        private readonly AuditQueryService _audit;
        private readonly QuestionnaireService _questionnaires;

        public AuditController(AuditQueryService audit, QuestionnaireService questionnaires)
        {
            _audit = audit;
            _questionnaires = questionnaires;
        }

        // A6. OJO: el parámetro NO puede llamarse "action" — colisiona con el token de ruta
        // {action} de MVC (nombre del método), que gana sobre el query string y rompía el filtro.
        public async Task<ActionResult> Index(string user = null, string act = null, string resource = null, string from = null, string to = null, int page = 1)
        {
            NormalizeDateRange(ref from, ref to);
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
                UserFilter = user, ActionFilter = act, ResourceFilter = resource,
                From = from, To = to, Page = page < 1 ? 1 : page, PageSize = pageSize
            };

            try
            {
                var all = await LoadFilteredAsync(user, act, resource, from, to);
                model.Total = all.Count;
                model.Entries.AddRange(all
                    .Skip((model.Page - 1) * pageSize)
                    .Take(pageSize));
            }
            catch (Exception)
            {
                model.Entries.Clear();
                model.Total = 0;
                ViewBag.DataError = "Ocurrió un error al procesar la solicitud.";
            }
            return View(model);
        }

        /// <summary>
        /// Aplica todos los filtros del listado: usuario/acción/fechas en la base y el
        /// filtro de "Recurso" (por título legible) en memoria, ya que el título no vive en
        /// la tabla de auditoría sino que se resuelve del cuestionario referenciado.
        /// </summary>
        private async Task<List<AuditEntry>> LoadFilteredAsync(string user, string act, string resource, string from, string to)
        {
            var fromUtc = ParseArgDateToUtc(from, endOfDay: false);
            var toUtc = ParseArgDateToUtc(to, endOfDay: true);

            // El tope y el filtrado los decide el servicio: acá sólo se arma la pantalla.
            var items = (await _audit.SearchAsync(user, act, fromUtc: fromUtc, toUtc: toUtc,
                                                 skip: 0, take: AuditQueryService.MaxFilas)).Items;
            var titles = await ResolveTitlesAsync(items);

            var res = (resource ?? string.Empty).Trim();
            var entries = new List<AuditEntry>();
            foreach (var e in items)
            {
                var av = AuditPresentation.Describe(e.EntityRef, e.Action);
                var label = ResourceLabel(av, titles, e.FhirJson);
                if (res.Length > 0 && (label ?? string.Empty).IndexOf(res, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                entries.Add(new AuditEntry
                {
                    Id = e.Id,
                    Timestamp = ArgentinaTime.FormatDateTime(e.Recorded),
                    Username = e.AgentUserId,
                    Action = av.Label,
                    ActionBadge = av.BadgeClass,
                    ActionIcon = av.Icon,
                    Resource = label,
                    IP = e.AgentIp
                });
            }
            return entries;
        }

        /// <summary>Fecha "yyyy-MM-dd" (local ARG) a límite UTC. endOfDay=true toma el día siguiente (cota superior exclusiva).</summary>
        private static DateTime? ParseArgDateToUtc(string date, bool endOfDay)
        {
            if (!DateTime.TryParseExact((date ?? string.Empty).Trim(), "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d))
                return null;
            return ArgentinaTime.ToUtc(endOfDay ? d.Date.AddDays(1) : d.Date);
        }

        /// <summary>
        /// Si "desde" quedó después de "hasta" (rango invertido, tipeado a mano en la URL o
        /// pegado sin pasar por el date-range picker), se intercambian acá. Nunca debe llegar a
        /// filtrarse con un rango negativo.
        /// </summary>
        private static void NormalizeDateRange(ref string from, ref string to)
        {
            var fd = ParseArgDateToUtc(from, endOfDay: false);
            var td = ParseArgDateToUtc(to, endOfDay: false);
            if (fd.HasValue && td.HasValue && fd.Value > td.Value)
            {
                var tmp = from; from = to; to = tmp;
            }
        }

        // A6b — Exportación. Reutiliza los mismos exporters (XLSX/CSV) que Reportes,
        // armando un ReportResult con las columnas visibles del listado.
        public async Task<ActionResult> Export(string user = null, string act = null, string resource = null, string from = null, string to = null, string format = "xlsx")
        {
            NormalizeDateRange(ref from, ref to);
            // Exporta exactamente lo que muestra el listado con los filtros aplicados (sin paginar).
            var entries = await LoadFilteredAsync(user, act, resource, from, to);
            var rows = entries.ConvertAll(e => new Dictionary<string, object>
            {
                ["Fecha"] = e.Timestamp,
                ["Usuario"] = e.Username,
                ["Acción"] = e.Action,
                ["Cuestionario"] = e.Resource,
                ["IP"] = e.IP
            });

            var result = new ReportResult { Total = rows.Count, Rows = rows };
            var dateSuffix = DateTime.Now.ToString("yyyy-MM-dd");
            if ((format ?? "xlsx").ToLowerInvariant() == "csv")
                return File(new CsvExporter().Export(result), "text/csv", $"auditoria_{dateSuffix}.csv");
            return File(new XlsxExporter().Export(result),
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
                    Resource = ResourceLabel(av, titles, e.FhirJson),
                    IP = e.AgentIp
                };

                // Para una publicación mostramos de qué versión a qué versión pasó y el diff.
                if (av.Operation == AuditPresentation.Op.Publish && av.ResourceId.HasValue)
                    await FillVersionDiffAsync(detail, av.ResourceId.Value, e.Recorded);

                // Para una consulta guardada mostramos su nombre, alcance y condiciones.
                if (av.ResourceType == "SavedReport")
                    await FillReportDetailAsync(detail, e.FhirJson);

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
            catch (Exception)
            {
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
        private async Task FillReportDetailAsync(AuditDetailViewModel detail, string fhirJson)
        {
            var snap = AuditPresentation.ReadSavedReport(fhirJson);
            if (snap == null) return;

            detail.HasReportDetail = true;
            detail.ReportName = snap.Name;
            detail.ReportScope = ScopeLabel(snap.Scope, snap.SpecialtyCode);

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
            }
            if (ids.Count == 0) return new Dictionary<Guid, string>();
            return await _questionnaires.GetTitlesAsync(ids);
        }

        /// <summary>Recurso legible: título del cuestionario si lo hay; si no, una ref limpia.</summary>
        private static string ResourceLabel(AuditPresentation.ActionView av, IReadOnlyDictionary<Guid, string> titles, string fhirJson = null)
        {
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
            if (av.ResourceType == "Report") return "Exportación de reporte";
            if (!string.IsNullOrWhiteSpace(av.ResourceType)) return av.ResourceType;
            return "—";
        }
    }
}
