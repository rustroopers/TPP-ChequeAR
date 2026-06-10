using System.Web.Mvc;

namespace ChequeAR.Api.Controllers.Mvc
{
    public class ErrorController : Controller
    {
        public ErrorController() { }

        public ActionResult Index(int code = 404)
        {
            Response.StatusCode = code;
            ViewBag.Code = code;
            ViewBag.Title = "Error " + code;
            return View();
        }
    }
}
