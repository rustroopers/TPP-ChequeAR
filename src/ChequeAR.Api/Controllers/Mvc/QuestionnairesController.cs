using System.Collections.Generic;
using System.Web.Mvc;
using ChequeAR.Api.Models;

namespace ChequeAR.Api.Controllers.Mvc
{
    public class QuestionnairesController : Controller
    {
        // Q1 — Listado
        public ActionResult Index(string q = null, string specialty = null, string status = null, int page = 1)
        {
            ViewBag.Title = "Cuestionarios";
            ViewBag.PageId = "questionnaires";
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Cuestionarios", (string)null)
            };

            var model = new QuestionnaireListViewModel
            {
                SearchTerm = q,
                SpecialtyFilter = specialty,
                StatusFilter = status,
                Page = page,
                PageSize = 20,
                Total = 47,
                Items = MockSeed.Questionnaires()
            };
            return View(model);
        }

        // Q2 — Editor (host de isla Vue)
        public ActionResult Edit(long id)
        {
            ViewBag.Title = "Editor de cuestionario";
            ViewBag.PageId = "questionnaires";
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Cuestionarios", Url.Action("Index")),
                System.Tuple.Create("Editar", (string)null)
            };
            ViewBag.QuestionnaireId = id;
            ViewBag.QuestionnaireTitle = "Cardio basal";
            ViewBag.Version = 3;
            return View();
        }

        // Q5 — Vista previa
        public ActionResult Preview(long id)
        {
            ViewBag.Title = "Vista previa";
            ViewBag.PageId = "questionnaires";
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Cuestionarios", Url.Action("Index")),
                System.Tuple.Create("Vista previa", (string)null)
            };

            var model = MockSeed.QuestionnaireDetail(id);
            return View(model);
        }

        // Q9 — Historial de versiones
        public ActionResult History(long id)
        {
            ViewBag.Title = "Historial de versiones";
            ViewBag.PageId = "questionnaires";
            ViewBag.QuestionnaireId = id;
            ViewBag.QuestionnaireTitle = "Cardio basal";
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Cuestionarios", Url.Action("Index")),
                System.Tuple.Create("Historial", (string)null)
            };

            return View(MockSeed.QuestionnaireVersions());
        }

        // Q6 / Q7 — Acciones (publicar / archivar) — POST stubs
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Publish(long id, string changeNote)
        {
            TempData["Toast"] = "Cuestionario publicado como nueva versión.";
            return RedirectToAction("Index");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Archive(long id)
        {
            TempData["Toast"] = "Cuestionario archivado.";
            return RedirectToAction("Index");
        }
    }
}
