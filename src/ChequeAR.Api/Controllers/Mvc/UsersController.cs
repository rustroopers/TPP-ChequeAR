using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using ChequeAR.Api.Auth;
using ChequeAR.Api.Models;
using ChequeAR.Application.Dtos;
using ChequeAR.Application.Exceptions;
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

        public async Task<ActionResult> Index(string q = null, int page = 1)
        {
            ViewBag.Title = "Usuarios";
            ViewBag.PageId = "users";
            const int pageSize = 50;
            var model = new UserListViewModel
            {
                Query = string.IsNullOrWhiteSpace(q) ? null : q.Trim(),
                PageSize = pageSize,
                Page = Math.Min(Math.Max(1, page), int.MaxValue / pageSize)
            };
            try
            {
                var cat = await _specialties.GetAsync();
                var pagina = await _users.ListAsync(model.Query, null, null, null, (model.Page - 1) * pageSize, pageSize);
                model.Total = pagina.Total;
                foreach (var u in pagina.Items)
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
                    Role = RoleHierarchy.Principal(u.Roles?.Select(r => r.Role)),
                    Revision = u.Revision
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
                ViewBag.InactiveAssigned = cat.Items
                    .Where(s => !s.IsActive && model.Specialties.Any(m => string.Equals(m.SpecialtyCode, s.Code, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                return View(model);
            }
            catch (Exception ex)
            {
                Helpers.HandledErrors.Log(this, ex);
                ViewBag.DataError = "No se pudo cargar la ficha del usuario. Volvé a intentarlo en unos minutos.";
                return View(new UserDetailViewModel { Username = username, FullName = username });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<ActionResult> Save(UserDetailViewModel model)
        {
            if (model == null || !ModelState.IsValid)
            {
                TempData["Toast"] = "No se guardó nada: el formulario llegó incompleto. Recargá la ficha y volvé a intentarlo.";
                TempData["ToastLevel"] = "error";
                return model?.Username == null
                    ? RedirectToAction("Index")
                    : RedirectToAction("Details", new { username = model.Username });
            }

            try
            {
                var existing = await _users.GetAsync(model.Username);
                if (existing == null) return HttpNotFound();

                // Sin ninguna casilla marcada el formulario no manda el campo: quedan sin especialidades.
                var codes = (model.SpecialtyCodes ?? new List<string>())
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct()
                    .ToList();
                // La pantalla ya no edita si la cuenta está activa: se conserva la actual.
                await _users.UpdateByAdminAsync(existing.Id, model.FullName, existing.IsActive, model.Role, codes,
                    CurrentUser.Id(User as System.Security.Claims.ClaimsPrincipal), model.Revision);

                SessionRevalidation.Invalidate(existing.Id);

                TempData["Toast"] = "Cambios guardados.";
                TempData["ToastLevel"] = "success";
            }
            catch (ConcurrencyConflictException ex)
            {
                TempData["Toast"] = ex.Message;
                TempData["ToastLevel"] = "error";
                return RedirectToAction("Details", new { username = model.Username });
            }
            catch (Exception ex) when (ex is BusinessRuleException || ex is ValidationFailedException)
            {
                TempData["Toast"] = ex.Message;
                TempData["ToastLevel"] = "error";
            }
            catch (Exception ex)
            {
                Helpers.HandledErrors.Log(this, ex);
                TempData["Toast"] = "No se pudieron guardar los cambios.";
                TempData["ToastLevel"] = "error";
            }
            return RedirectToAction("Index");
        }
    }
}
