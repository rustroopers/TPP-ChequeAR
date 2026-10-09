using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Mvc;
using System.Web.Security;
using ChequeAR.Api.Auth;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Services;

namespace ChequeAR.Api.Controllers.Mvc
{
    public class AccountController : Controller
    {
        private readonly ICredentialAuthenticator _auth;
        private readonly UserService _users;
        private readonly SpecialtyCatalogService _specialties;

        public AccountController(ICredentialAuthenticator auth, UserService users, SpecialtyCatalogService specialties)
        {
            _auth = auth;
            _users = users;
            _specialties = specialties;
        }

        [HttpGet, AllowAnonymous]
        public ActionResult Login(string returnUrl = null)
        {
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
        public async Task<ActionResult> DoLogin(string username, string password, bool remember = false, string returnUrl = null)
        {
            // Se preserva en TODO re-render de la vista, incluidos los de error: antes se
            // perdía apenas el login fallaba, y un login con returnUrl que fallaba al
            // primer intento mandaba a Home en el segundo en vez de a donde el usuario
            // efectivamente quería ir.
            ViewBag.ReturnUrl = returnUrl;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                ModelState.AddModelError("", "Ingresá usuario y contraseña.");
                return View("Login");
            }

            try
            {
                var info = await _auth.LoginAsync(username, password);
                if (info == null)
                {
                    // Mismo mensaje para usuario inexistente, inactivo, sin contraseña
                    // configurada o contraseña incorrecta: no hay que darle a quien intenta
                    // loguearse una pista de cuál de esas cuatro cosas pasó.
                    ModelState.AddModelError("", "Usuario o contraseña inválidos.");
                    return View("Login");
                }

                // El espejo (AppUser/UserRole/UserSpecialty) se actualiza ANTES de emitir la
                // cookie: si el sync falla, no se emite una sesión con permisos que la base
                // no respalda. Devuelve el UserInfo SANEADO (códigos de especialidad
                // filtrados contra el catálogo, rol colapsado a uno solo) para que la
                // cookie no lleve algo que la base rechazó.
                info = await _users.SyncFromHospitalAsync(info);

                // SetAuthCookie guardaria solo el nombre y perderia info.Roles, que es
                // justo lo que necesita SessionToken para firmar el bearer de las islas.
                SessionCookie.Issue(HttpContext, info, remember);

                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return Redirect(returnUrl);
                }
                return RedirectToAction("Index", "Home");
            }
            catch (Exception ex)
            {
                Helpers.HandledErrors.Log(this, ex);
                ModelState.AddModelError("", "No se pudo iniciar sesión. Intentá nuevamente.");
                return View("Login");
            }
        }

        [HttpGet, AllowAnonymous]
        public ActionResult SessionExpired()
        {
            return View();
        }

        // 'new' explícito: Controller expone una propiedad Profile (perfil de ASP.NET, que el
        // proyecto no usa). Esta acción la oculta a propósito — sin la palabra clave el
        // compilador avisa que podría ser un descuido.
        [Authorize]
        public new async Task<ActionResult> Profile()
        {
            ViewBag.Title = "Mi perfil";
            var principal = User as System.Security.Claims.ClaimsPrincipal;
            var id = ChequeAR.Api.Auth.CurrentUser.Id(principal);
            var cat = await _specialties.GetAsync();

            var model = new ChequeAR.Api.Models.ProfileViewModel
            {
                UserName = principal?.Identity?.Name,
                DisplayName = principal?.Identity?.Name,
                Roles = new List<string>(ChequeAR.Api.Auth.CurrentUser.Roles(principal))
            };
            // El claim 'specialty' ya trae las especialidades de UserSpecialty (via
            // JwtIssuer/SessionCookie): es la vía barata, no hace falta ir a la base.
            foreach (var code in ChequeAR.Api.Auth.CurrentUser.Specialties(principal))
            {
                model.Specialties.Add(new ChequeAR.Api.Models.SpecialtyChipView
                {
                    Code = code, Label = cat.Label(code), Color = cat.Color(code)
                });
            }

            try
            {
                var u = await _users.GetAsync(id);
                if (u != null)
                {
                    model.DisplayName = u.DisplayName ?? model.DisplayName;
                    model.Email = u.Email;
                }
            }
            catch (Exception) { /* la pantalla no se cae por no poder leer el espejo del padrón */ }

            return View(model);
        }

        [Authorize, HttpPost, ValidateAntiForgeryToken]
        public ActionResult SaveProfile()
        {
            return RedirectToAction("Profile");
        }

        [Authorize]
        public ActionResult Logout()
        {
            FormsAuthentication.SignOut();
            return RedirectToAction("Login");
        }
    }
}
