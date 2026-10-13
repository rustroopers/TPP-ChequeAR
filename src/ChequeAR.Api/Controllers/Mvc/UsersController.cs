using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using ChequeAR.Api.Auth;
using ChequeAR.Api.Models;
using ChequeAR.Application.Dtos;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Api.Controllers.Mvc
{
    [RequiresPermission(Permissions.UserManage)]
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
            var model = new UserListViewModel();
            try
            {
                var cat = await _specialties.GetAsync();
                var items = (await _users.ListAsync(null, null, null, null, 0, 100)).Items;
                foreach (var u in items)
                {
                    // RoleHierarchy.Principal, no "el primer rol de la colección": UserRole es
                    // una colección por herencia del espejo del hospital, pero el modelo de
                    // ChequeAR asume un único rol por usuario (ver Domain.Enums.Role). Antes
                    // esto llamaba GetEnumerator().Current sin MoveNext() y devolvía siempre
                    // null -> la columna "Rol" se mostraba rota como "Médico" a secas.
                    var roleNames = u.Roles?.Select(r => r.Role) ?? Enumerable.Empty<string>();
                    var role = RoleHierarchy.Principal(roleNames);

                    var spec = "—";
                    if (u.Specialties != null && u.Specialties.Count > 0)
                    {
                        // Nombre legible, no el código: la tabla mostraba "reuma" donde
                        // debía decir "Reumatología". Se mapea acá y no en la vista para
                        // no dejar el "—" (sin especialidad) a merced del default de Label.
                        spec = string.Join(", ", u.Specialties.Select(s => cat.Label(s.SpecialtyCode)));
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
                Helpers.HandledErrors.Log(this, ex);
                model.Users.Clear();
                ViewBag.DataError = "Ocurrió un error al procesar la solicitud.";
            }
            return View(model);
        }

        public async Task<ActionResult> Details(string username)
        {
            ViewBag.Title = "Editar usuario";
            ViewBag.PageId = "users";
            try
            {
                var u = await _users.GetAsync(username);
                if (u == null) return HttpNotFound();
                var cat = await _specialties.GetAsync();
                var model = new UserDetailViewModel
                {
                    Username = u.Id, FullName = u.DisplayName, Email = u.Email,
                    Active = u.IsActive,
                    Role = RoleHierarchy.Principal(u.Roles?.Select(r => r.Role))
                };
                if (u.Specialties != null)
                {
                    foreach (var s in u.Specialties)
                    {
                        model.Specialties.Add(new UserSpecialtyItem
                        {
                            SpecialtyCode = s.SpecialtyCode,
                            SpecialtyLabel = cat.Label(s.SpecialtyCode),
                            SpecialtyColor = cat.Color(s.SpecialtyCode)
                        });
                    }
                }
                // Catálogo completo (activo), para que la vista pueda ofrecer un selector de
                // especialidades a asignar además de las que ya tiene.
                ViewBag.AvailableSpecialties = cat.Items.Where(s => s.IsActive).ToList();
                return View(model);
            }
            catch (Exception ex)
            {
                Helpers.HandledErrors.Log(this, ex);
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

                // Rol: único y global (ver Domain.Enums.Role). SetRoleAsync valida que el
                // valor sea conocido y que el sistema no se quede sin ningún Administrador.
                await _users.SetRoleAsync(existing.Id, model.Role, actingUserId: User.Identity.Name);

                // Especialidades: antes Specialties[i].SpecialtyCode se posteaba y se
                // descartaba en silencio (UpsertAsync ignora colecciones). Ahora se
                // reconcilia de verdad vía el mismo camino que el sync on-login, con una
                // lista EXPLÍCITA (nunca null: eso significaría "no la toques", que es lo
                // que se necesita para el sync del hospital pero no para un ABM manual,
                // donde el admin siempre manda el estado completo deseado). Roles = null:
                // ya se manejó arriba, y null significa "no tocar roles" para este método.
                var codes = (model.Specialties ?? new List<UserSpecialtyItem>())
                    .Select(s => s.SpecialtyCode)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                await _users.SyncFromHospitalAsync(new UserInfo
                {
                    Id = existing.Id,
                    DisplayName = existing.DisplayName,
                    Roles = null,
                    Specialties = codes
                });

                TempData["Toast"] = "Cambios guardados.";
                TempData["ToastLevel"] = "success";
            }
            catch (InvalidOperationException ex)
            {
                TempData["Toast"] = ex.Message;
                TempData["ToastLevel"] = "error";
            }
            catch (ArgumentException ex)
            {
                TempData["Toast"] = ex.Message;
                TempData["ToastLevel"] = "error";
            }
            catch (System.Exception)
            {
                TempData["Toast"] = "No se pudieron guardar los cambios.";
                TempData["ToastLevel"] = "error";
            }
            return RedirectToAction("Index");
        }
    }
}
