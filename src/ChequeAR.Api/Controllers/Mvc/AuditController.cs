using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using ChequeAR.Api.Helpers;
using ChequeAR.Api.Models;
using ChequeAR.Application.Exporters;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Reports;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Api.Controllers.Mvc
{
    // TODO: re-habilitar [Authorize(Roles = Role.Administrador + "," + Role.Jefe)] cuando FormsAuth esté wired.
    public class AuditController : Controller
    {
        private readonly IAuditRepository _repo;
        private readonly IQuestionnaireRepository _questionnaires;

        public AuditController(IAuditRepository repo, IQuestionnaireRepository questionnaires)
        {
            _repo = repo;
            _questionnaires = questionnaires;
        }

        // A6. OJO: el parámetro NO puede llamarse "action" — colisiona con el token de ruta
        // {action} de MVC (nombre del método), que gana sobre el query string y rompía el filtro.
        public async Task<ActionResult> Index(string user = null, string act = null, string resource = null, string from = null, string to = null, int page = 1)
        {
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
            catch (Exception ex)
            {
                model.Entries.Clear();
                model.Total = 0;
                ViewBag.DataError = ex.Message;
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

            var items = await _repo.SearchAsync(user, act, fromUtc, toUtc, 5000);
            var titles = await ResolveTitlesAsync(items);

            var res = (resource ?? string.Empty).Trim();
            var entries = new List<AuditEntry>();
            foreach (var e in items)
            {
                var av = AuditPresentation.Describe(e.EntityRef, e.Action);
                var label = ResourceLabel(av, titles);
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

        // A6b — Exportación. Reutiliza los mismos exporters (XLSX/CSV) que Reportes,
        // armando un ReportResult con las columnas visibles del listado.
        public async Task<ActionResult> Export(string user = null, string act = null, string resource = null, string from = null, string to = null, string format = "xlsx")
        {
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
            if ((format ?? "xlsx").ToLowerInvariant() == "csv")
                return File(new CsvExporter().Export(result), "text/csv", "auditoria.csv");
            return File(new XlsxExporter().Export(result),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "auditoria.xlsx");
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
                var all = await _repo.SearchAsync(null, null, null, null, 5000);
                var e = all.FirstOrDefault(x => x.Id == id);
                if (e == null) return HttpNotFound();

                var av = AuditPresentation.Describe(e.EntityRef, e.Action);
                var titles = await ResolveTitlesAsync(new[] { e });

                var detail = new AuditDetailViewModel
                {
                    Id = e.Id,
                    Timestamp = ArgentinaTime.FormatDateTime(e.Recorded),
                    Username = e.AgentUserId,
                    Action = av.Label,
                    ActionBadge = av.BadgeClass,
                    ActionIcon = av.Icon,
                    Resource = ResourceLabel(av, titles),
                    IP = e.AgentIp
                };

                // Para una publicación mostramos de qué versión a qué versión pasó y el diff.
                if (av.Operation == AuditPresentation.Op.Publish && av.ResourceId.HasValue)
                    await FillVersionDiffAsync(detail, av.ResourceId.Value, e.Recorded);

                return View(detail);
            }
            catch (Exception ex)
            {
                ViewBag.DataError = ex.Message;
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
        private static string ResourceLabel(AuditPresentation.ActionView av, IReadOnlyDictionary<Guid, string> titles)
        {
            if (av.ResourceType == "Questionnaire" && av.ResourceId.HasValue
                && titles.TryGetValue(av.ResourceId.Value, out var title)
                && !string.IsNullOrWhiteSpace(title))
                return title;

            if (av.ResourceType == "QuestionnaireResponse") return "Respuesta de cuestionario";
            if (av.ResourceType == "Report") return "Exportación de reporte";
            if (!string.IsNullOrWhiteSpace(av.ResourceType)) return av.ResourceType;
            return "—";
        }
    }
}
