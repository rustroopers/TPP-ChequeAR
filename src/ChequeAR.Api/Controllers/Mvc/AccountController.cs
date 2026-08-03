using System;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using System.Web.Security;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Services;

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
