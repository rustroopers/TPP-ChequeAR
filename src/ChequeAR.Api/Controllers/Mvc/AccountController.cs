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
                var error = await SignInAsync(username, password, remember);
                if (error != null)
                {
                    ModelState.AddModelError("", error);
                    return View("Login");
                }

                // Un POST a Logout sin sesión deja ReturnUrl=/Account/Logout, que por GET da 404.
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
                    && !returnUrl.StartsWith("/Account/", StringComparison.OrdinalIgnoreCase))
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

        /// <summary>
        /// Volver a entrar desde el aviso de sesión vencida, sin salir de la página ni perder lo
        /// cargado. Sin antiforgery: el token quedó atado a la sesión vencida. El header propio
        /// no se puede mandar desde otro origen sin CORS.
        /// </summary>
        [HttpPost, AllowAnonymous]
        public async Task<ActionResult> Reauth(string username, string password)
        {
            Response.SuppressFormsAuthenticationRedirect = true;
            Response.TrySkipIisCustomErrors = true;
            if (Request.Headers["X-CQ-Reauth"] != "1") return new HttpStatusCodeResult(400);
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                Response.StatusCode = 400;
                return Json(new { error = "Usuario o contraseña inválidos." });
            }

            try
            {
                var error = await SignInAsync(username, password, false);
                if (error == null) return Json(new { ok = true });
                if (Response.StatusCode != 429) Response.StatusCode = 401;
                return Json(new { error });
            }
            catch (Exception ex)
            {
                Helpers.HandledErrors.Log(this, ex);
                Response.StatusCode = 500;
                return Json(new { error = "No se pudo iniciar sesión. Intentá nuevamente." });
            }
        }

        /// <summary>Valida, sincroniza y emite la cookie. Null si entró; si no, el mensaje para la persona.</summary>
        private async Task<string> SignInAsync(string username, string password, bool remember)
        {
            var ip = Request.UserHostAddress;
            if (LoginThrottle.IsBlocked(ip))
            {
                Serilog.Log.Warning("Login rechazado por exceso de intentos desde {Ip}", ip);
                Response.StatusCode = 429;
                return "Demasiados intentos de inicio de sesión. Esperá unos minutos y volvé a probar.";
            }

            // Mismo mensaje para usuario inexistente, inactivo, sin contraseña configurada,
            // bloqueado o contraseña incorrecta: no hay que darle a quien intenta loguearse una
            // pista de cuál de esas cosas pasó.
            const string invalid = "Usuario o contraseña inválidos. Después de varios intentos fallidos el acceso se bloquea por unos minutos.";
            var info = await _auth.LoginAsync(username, password);
            if (info == null)
            {
                LoginThrottle.RegisterFailure(ip);
                Serilog.Log.Warning("Login fallido para {User} desde {Ip}", await _users.LoggableUserNameAsync(username), ip);
                return invalid;
            }

            // El espejo (AppUser/UserRole/UserSpecialty) se actualiza ANTES de emitir la
            // cookie: si el sync falla, no se emite una sesión con permisos que la base
            // no respalda. Devuelve el UserInfo SANEADO (códigos de especialidad
            // filtrados contra el catálogo, rol colapsado a uno solo) para que la
            // cookie no lleve algo que la base rechazó.
            info = await _users.SyncFromHospitalAsync(info);
            if (info == null) return invalid;

            // SetAuthCookie guardaria solo el nombre y perderia info.Roles, que es
            // justo lo que necesita SessionToken para firmar el bearer de las islas.
            SessionCookie.Issue(HttpContext, info, remember);
            return null;
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

        /// <summary>Renueva el JWT de las islas. GET: otro origen puede dispararlo pero no leer la respuesta.</summary>
        [Authorize, HttpGet]
        [OutputCache(NoStore = true, Duration = 0, Location = System.Web.UI.OutputCacheLocation.None)]
        public ActionResult Token()
        {
            var token = SessionToken.IssueFor(User);
            if (string.IsNullOrEmpty(token)) return new HttpStatusCodeResult(401);
            return Json(new { token }, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Rota el sello de seguridad: invalida las copias de la cookie y los JWT emitidos.</summary>
        [Authorize, HttpPost, ValidateAntiForgeryToken]
        public async Task<ActionResult> Logout()
        {
            var id = CurrentUser.Id(User as System.Security.Claims.ClaimsPrincipal);
            try
            {
                if (!string.IsNullOrEmpty(id))
                {
                    await _users.RotateSecurityStampAsync(id);
                    SessionRevalidation.Invalidate(id);
                }
            }
            catch (Exception ex)
            {
                // Sin base la cookie se borra igual.
                Helpers.HandledErrors.Log(this, ex);
            }
            FormsAuthentication.SignOut();
            return RedirectToAction("Login");
        }
    }
}
