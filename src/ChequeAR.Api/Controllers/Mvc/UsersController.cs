using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Mvc;
using ChequeAR.Api.Models;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Api.Controllers.Mvc
{
    [Authorize(Roles = Role.Administrador)]
    public class UsersController : Controller
    {
        private readonly IUserRepository _repo;
        public UsersController(IUserRepository repo) { _repo = repo; }

        public async Task<ActionResult> Index()
        {
            ViewBag.Title = "Usuarios";
            ViewBag.PageId = "users";
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Usuarios", (string)null)
            };
            var model = new UserListViewModel();
            try
            {
                var items = await _repo.ListAsync(null, null, null, null, 0, 100);
                foreach (var u in items)
                {
                    var role = "—"; var spec = "—";
                    if (u.Roles != null && u.Roles.Count > 0) role = u.Roles.GetEnumerator().Current?.Role ?? "Médico";
                    if (u.Specialties != null && u.Specialties.Count > 0)
                    {
                        var e = u.Specialties.GetEnumerator();
                        if (e.MoveNext()) spec = e.Current.SpecialtyCode;
                    }
                    model.Users.Add(new UserListItem
                    {
                        Username = u.Id, FullName = u.DisplayName,
                        Role = role, Specialty = spec, Active = u.IsActive
                    });
                }
            }
            catch
            {
                model.Users = MockSeed.Users();
                ViewBag.DataMode = "demo";
            }
            return View(model);
        }

        public async Task<ActionResult> Details(string username)
        {
            ViewBag.Title = "Editar usuario";
            ViewBag.PageId = "users";
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Usuarios", Url.Action("Index")),
                System.Tuple.Create(username, (string)null)
            };
            try
            {
                var u = await _repo.GetAsync(username);
                if (u == null) return HttpNotFound();
                var model = new UserDetailViewModel
                {
                    Username = u.Id, FullName = u.DisplayName, Email = u.Email,
                    Active = u.IsActive
                };
                if (u.Specialties != null)
                {
                    foreach (var s in u.Specialties)
                    {
                        model.SpecialtyRoles.Add(new UserSpecialtyRole
                        {
                            SpecialtyCode = s.SpecialtyCode,
                            SpecialtyLabel = s.SpecialtyCode,
                            Role = "Médico"
                        });
                    }
                }
                return View(model);
            }
            catch
            {
                ViewBag.DataMode = "demo";
                return View(MockSeed.UserDetail(username));
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<ActionResult> Save(UserDetailViewModel model)
        {
            try
            {
                var existing = await _repo.GetAsync(model.Username);
                if (existing == null) return HttpNotFound();
                existing.DisplayName = model.FullName ?? existing.DisplayName;
                existing.IsActive = model.Active;
                await _repo.UpsertAsync(existing);
                TempData["Toast"] = "Cambios guardados.";
            }
            catch (System.Exception ex)
            {
                TempData["Toast"] = "Error: " + ex.Message;
            }
            return RedirectToAction("Index");
        }
    }
}
