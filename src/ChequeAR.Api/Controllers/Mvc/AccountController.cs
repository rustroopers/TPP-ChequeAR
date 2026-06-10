using System;
using System.Web;
using System.Web.Mvc;

namespace ChequeAR.Api.Controllers.Mvc
{
    public class AccountController : Controller
    {
        // Cambio de idioma — setea cookie y vuelve a la URL previa.
        public ActionResult SetLanguage(string lang, string returnUrl)
        {
            if (lang != "es-AR" && lang != "en")
            {
                lang = "es-AR";
            }
            var cookie = new HttpCookie("chequear_lang", lang)
            {
                Expires = DateTime.UtcNow.AddDays(30),
                Path = "/",
                HttpOnly = false
            };
            Response.Cookies.Add(cookie);

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction("Index", "Home");
        }

        public ActionResult Profile()
        {
            ViewBag.Title = "Mi perfil";
            return View();
        }

        public ActionResult Logout()
        {
            // Stub — la lógica real vive en /Controllers/Api/AuthController (spec 005).
            return RedirectToAction("Index", "Home");
        }
    }
}
