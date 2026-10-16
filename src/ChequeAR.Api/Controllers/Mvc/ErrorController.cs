using System.Linq;
using System.Web.Mvc;

namespace ChequeAR.Api.Controllers.Mvc
{
    public class ErrorController : Controller
    {
        public ErrorController() { }

        private static readonly int[] Codes = { 401, 403, 404, 500 };

        public ActionResult Index(int code = 404)
        {
            if (!Codes.Contains(code)) code = 500;
            Response.StatusCode = code;
            // Sin esto, IIS en Azure reemplaza la vista por su propia página de error.
            Response.TrySkipIisCustomErrors = true;
            ViewBag.Code = code;
            ViewBag.Title = "Error " + code;
            return View();
        }
    }
}
