using System.Collections.Generic;
using System.Web.Mvc;
using ChequeAR.Api.Models;

namespace ChequeAR.Api.Controllers.Mvc
{
    public class UsersController : Controller
    {
        // A4 — Listado de usuarios
        public ActionResult Index()
        {
            ViewBag.Title = "Usuarios";
            ViewBag.PageId = "users";
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Usuarios", (string)null)
            };
            return View(new UserListViewModel { Users = MockSeed.Users() });
        }

        // A5 — Detalle
        public ActionResult Details(string username)
        {
            ViewBag.Title = "Editar usuario";
            ViewBag.PageId = "users";
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Usuarios", Url.Action("Index")),
                System.Tuple.Create(username, (string)null)
            };
            return View(MockSeed.UserDetail(username));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Save(UserDetailViewModel model)
        {
            TempData["Toast"] = "Cambios guardados.";
            return RedirectToAction("Index");
        }
    }
}
