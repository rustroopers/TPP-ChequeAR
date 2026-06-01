using System.Web.Mvc;

namespace ChequeAR.Api.Controllers.Mvc
{
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            ViewBag.Title = "ChequeAR";
            return View("~/Views/Home/Index.cshtml");
        }
    }
}
