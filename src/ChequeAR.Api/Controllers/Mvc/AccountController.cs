using System;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using System.Web.Security;
using ChequeAR.Application.Interfaces;

namespace ChequeAR.Api.Controllers.Mvc
{
    public class AccountController : Controller
    {
        private readonly IHospitalAuthClient _hospitalAuth;
        private readonly IUserRepository _userRepo;

        public AccountController(IHospitalAuthClient hospitalAuth, IUserRepository userRepo)
        {
            _hospitalAuth = hospitalAuth;
            _userRepo = userRepo;
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

                FormsAuthentication.SetAuthCookie(info.Id ?? username, remember);

                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return Redirect(returnUrl);
                }
                return RedirectToAction("Index", "Home");
            }
            catch (Exception ex)
            {
                // En dev sin endpoint del hospital: aceptamos cualquier credencial para demo.
                if (System.Web.HttpContext.Current.IsDebuggingEnabled)
                {
                    FormsAuthentication.SetAuthCookie(username, remember);
                    return RedirectToAction("Index", "Home");
                }
                ModelState.AddModelError("", "Error de autenticación: " + ex.Message);
                return View("Login");
            }
        }

        [HttpGet, AllowAnonymous]
        public ActionResult SessionExpired()
        {
            return View();
        }

        // T3 — cambio de idioma
        public ActionResult SetLanguage(string lang, string returnUrl)
        {
            if (lang != "es-AR" && lang != "en") lang = "es-AR";
            var cookie = new HttpCookie("chequear_lang", lang)
            {
                Expires = DateTime.UtcNow.AddDays(30),
                Path = "/",
                HttpOnly = false
            };
            Response.Cookies.Add(cookie);
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) return Redirect(returnUrl);
            return RedirectToAction("Index", "Home");
        }

        public ActionResult Profile() { ViewBag.Title = "Mi perfil"; return View(); }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult SaveProfile(string lang)
        {
            return SetLanguage(lang, Url.Action("Profile"));
        }

        public ActionResult Logout()
        {
            FormsAuthentication.SignOut();
            return RedirectToAction("Login");
        }
    }
}
