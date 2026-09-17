using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using ChequeAR.Api.Models;
using ChequeAR.Application.Dtos;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Api.Controllers.Mvc
{
    [Authorize(Roles = Role.Administrador)]
    public class UsersController : Controller
    {
        private readonly UserService _users;
        private readonly SpecialtyCatalogService _specialties;

        public UsersController(UserService users, SpecialtyCatalogService specialties)
        {
            _users = users;
            _specialties = specialties;
        }

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
                var cat = await _specialties.GetAsync();
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
                        if (e.MoveNext()) spec = cat.Label(e.Current.SpecialtyCode);
                    }
                    model.Users.Add(new UserListItem
                    {
                        Username = u.Id, FullName = u.DisplayName,
                        Role = role, Specialty = spec, Active = u.IsActive
                    });
                }
            }
            catch (Exception)
            {
                model.Users.Clear();
                ViewBag.DataError = "Ocurrió un error al procesar la solicitud.";
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
                var cat = await _specialties.GetAsync();
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
                            SpecialtyLabel = cat.Label(s.SpecialtyCode),
                            SpecialtyColor = cat.Color(s.SpecialtyCode),
                            Role = "Médico"
                        });
                    }
                }
                // Catálogo completo (activo), para que la vista pueda ofrecer un selector de
                // especialidades a asignar además de las que ya tiene.
                ViewBag.AvailableSpecialties = cat.Items.Where(s => s.IsActive).ToList();
                return View(model);
            }
            catch (Exception)
            {
                ViewBag.DataError = "Ocurrió un error al procesar la solicitud.";
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

                // DisplayName/IsActive: edición administrativa simple, no toca membresías.
                existing.DisplayName = model.FullName ?? existing.DisplayName;
                existing.IsActive = model.Active;
                await _users.UpsertAsync(existing);

                // Especialidades: antes SpecialtyRoles[i].SpecialtyCode se posteaba y se
                // descartaba en silencio (UpsertAsync ignora colecciones). Ahora se
                // reconcilia de verdad vía el mismo camino que el sync on-login, con una
                // lista EXPLÍCITA (nunca null: eso significaría "no la toques", que es lo
                // que se necesita para el sync del hospital pero no para un ABM manual,
                // donde el admin siempre manda el estado completo deseado).
                var codes = (model.SpecialtyRoles ?? new List<UserSpecialtyRole>())
                    .Select(s => s.SpecialtyCode)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var roles = (existing.Roles ?? new List<ChequeAR.Domain.Entities.UserRole>())
                    .Select(r => r.Role).ToList();
                await _users.SyncFromHospitalAsync(new UserInfo
                {
                    Id = existing.Id,
                    DisplayName = existing.DisplayName,
                    Roles = roles,
                    Specialties = codes
                });

                TempData["Toast"] = "Cambios guardados.";
            }
            catch (System.Exception)
            {
                TempData["Toast"] = "No se pudieron guardar los cambios.";
            }
            return RedirectToAction("Index");
        }
    }
}
