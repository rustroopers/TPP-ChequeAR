using System;
using System.Collections.Generic;
using System.Configuration;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using System.Web.Security;
using ChequeAR.Api.Auth;
using ChequeAR.Application.Dtos;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Api.Controllers.Mvc
{
    public class AccountController : Controller
    {
        private readonly IHospitalAuthClient _hospitalAuth;
        private readonly UserService _users;

        public AccountController(IHospitalAuthClient hospitalAuth, UserService users)
        {
            _hospitalAuth = hospitalAuth;
            _users = users;
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
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                ModelState.AddModelError("", "Ingresá usuario y contraseña.");
                return View("Login");
            }

            try
            {
                var info = await _hospitalAuth.LoginAsync(username, password);
                if (info == null)
                {
                    ModelState.AddModelError("", "Usuario o contraseña inválidos.");
                    return View("Login");
                }

                // SetAuthCookie guardaria solo el nombre y perderia info.Roles, que es
                // justo lo que necesita SessionToken para firmar el bearer de las islas.
                SessionCookie.Issue(HttpContext, info, remember);

                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return Redirect(returnUrl);
                }
                return RedirectToAction("Index", "Home");
            }
            catch (Exception)
            {
                // Fallback de demo cuando el hospital no está disponible: requiere debug=true
                // (Web.config) Y un opt-in explícito por entorno (Auth:AllowDebugLoginFallback en
                // Web.Secrets.config, no versionado), para que no baste con un debug="true" suelto
                // en un ambiente que no sea la máquina de un dev.
                var allowDebugFallback = string.Equals(
                    ConfigurationManager.AppSettings["Auth:AllowDebugLoginFallback"], "true",
                    StringComparison.OrdinalIgnoreCase);
                if (System.Web.HttpContext.Current.IsDebuggingEnabled && allowDebugFallback)
                {
                    SessionCookie.Issue(HttpContext, new UserInfo
                    {
                        Id = username,
                        DisplayName = username,
                        Roles = new List<string> { Role.Medico },
                        Specialties = new List<string>()
                    }, remember);
                    return RedirectToAction("Index", "Home");
                }
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
        public new ActionResult Profile() { ViewBag.Title = "Mi perfil"; return View(); }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult SaveProfile()
        {
            return RedirectToAction("Profile");
        }

        public ActionResult Logout()
        {
            FormsAuthentication.SignOut();
            return RedirectToAction("Login");
        }
    }
}
