using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using ChequeAR.Api.Helpers;
using ChequeAR.Api.Models;
using ChequeAR.Application.Interfaces;
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

        // A6
        public async Task<ActionResult> Index(string user = null, string action = null, string resource = null, string from = null, string to = null, int page = 1)
        {
            ViewBag.Title = "Auditoría";
            ViewBag.PageId = "audit";
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Auditoría", (string)null)
            };
            var model = new AuditListViewModel
            {
                UserFilter = user, ActionFilter = action, ResourceFilter = resource,
                From = from, To = to, Page = page
            };

            try
            {
                var items = await _repo.SearchAsync(user, resource, (page - 1) * 50, 50);

                // Resolvemos todos los títulos de cuestionario de la página de una sola query.
                var titles = await ResolveTitlesAsync(items);

                foreach (var e in items)
                {
                    var av = AuditPresentation.Describe(e.EntityRef, e.Action);
                    model.Entries.Add(new AuditEntry
                    {
                        Id = e.Id,
                        Timestamp = ArgentinaTime.FormatDateTime(e.Recorded),
                        Username = e.AgentUserId,
                        Action = av.Label,
                        ActionBadge = av.BadgeClass,
                        ActionIcon = av.Icon,
                        Resource = ResourceLabel(av, titles),
                        IP = e.AgentIp
                    });
                }
                model.Total = model.Entries.Count;
            }
            catch (Exception ex)
            {
                model.Entries.Clear();
                model.Total = 0;
                ViewBag.DataError = ex.Message;
            }
            return View(model);
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
                var all = await _repo.SearchAsync(null, null, 0, 5000);
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
