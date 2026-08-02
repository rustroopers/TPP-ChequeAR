using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Mvc;
using ChequeAR.Api.Models;
using ChequeAR.Application.Services;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Api.Controllers.Mvc
{
    // TODO: re-habilitar [Authorize(Roles = Role.Administrador)] cuando FormsAuth esté wired.
    public class UsersController : Controller
    {
        private readonly UserService _users;
        public UsersController(UserService users) { _users = users; }

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
                var items = (await _users.ListAsync(null, null, null, null, 0, 100)).Items;
                foreach (var u in items)
                {
                    var role = "—"; var spec = "—";
                    if (u.Roles != null && u.Roles.Count > 0) role = u.Roles.GetEnumerator().Current?.Role ?? "Médico";
                    if (u.Specialties != null && u.Specialties.Count > 0)
                    {
                        var e = u.Specialties.GetEnumerator();
                        // Nombre legible, no el código: la tabla mostraba "reuma" donde
                        // debía decir "Reumatología". Se mapea acá y no en la vista para
                        // no dejar el "—" (sin especialidad) a merced del default de Label.
                        if (e.MoveNext()) spec = SpecialtyMap.Label(e.Current.SpecialtyCode);
                    }
                    model.Users.Add(new UserListItem
                    {
                        Username = u.Id, FullName = u.DisplayName,
                        Role = role, Specialty = spec, Active = u.IsActive
                    });
                }
            }
            catch (Exception ex)
            {
                model.Users.Clear();
                ViewBag.DataError = ex.Message;
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
                var u = await _users.GetAsync(username);
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
            catch (Exception ex)
            {
                ViewBag.DataError = ex.Message;
                return View(new UserDetailViewModel { Username = username });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<ActionResult> Save(UserDetailViewModel model)
        {
            try
            {
                var existing = await _users.GetAsync(model.Username);
                if (existing == null) return HttpNotFound();
                existing.DisplayName = model.FullName ?? existing.DisplayName;
                existing.IsActive = model.Active;
                await _users.UpsertAsync(existing);
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
