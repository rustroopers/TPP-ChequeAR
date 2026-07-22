using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using ChequeAR.Api.Models;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Api.Controllers.Mvc
{
    // TODO: re-habilitar [Authorize(Roles = Role.Administrador + "," + Role.Jefe)] cuando FormsAuth esté wired.
    public class AuditController : Controller
    {
        private readonly IAuditRepository _repo;
        public AuditController(IAuditRepository repo) { _repo = repo; }

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
                foreach (var e in items)
                {
                    model.Entries.Add(new AuditEntry
                    {
                        Id = e.Id,
                        Timestamp = e.Recorded.ToString("yyyy-MM-dd HH:mm"),
                        Username = e.AgentUserId,
                        Action = ActionLabel(e.Action),
                        Resource = e.EntityRef,
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
                var detail = new AuditDetailViewModel
                {
                    Id = e.Id,
                    Timestamp = e.Recorded.ToString("yyyy-MM-dd HH:mm:ss"),
                    Username = e.AgentUserId,
                    Action = ActionLabel(e.Action),
                    Resource = e.EntityRef,
                    IP = e.AgentIp
                };
                return View(detail);
            }
            catch (Exception ex)
            {
                ViewBag.DataError = ex.Message;
                return View(new AuditDetailViewModel { Id = id });
            }
        }

        private static string ActionLabel(string code) =>
            code == "C" ? "CREATE" : code == "R" ? "READ" : code == "U" ? "UPDATE" : code == "D" ? "DELETE" : code == "E" ? "EXECUTE" : code;
    }
}
