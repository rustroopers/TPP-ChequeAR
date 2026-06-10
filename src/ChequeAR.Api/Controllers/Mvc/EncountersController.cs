using System.Collections.Generic;
using System.Web.Mvc;
using ChequeAR.Api.Models;

namespace ChequeAR.Api.Controllers.Mvc
{
    public class EncountersController : Controller
    {
        // R1 — Buscador de paciente
        public ActionResult Search(string q = null)
        {
            ViewBag.Title = "Pacientes";
            ViewBag.PageId = "encounters";
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Pacientes", (string)null)
            };

            var model = new PatientSearchViewModel
            {
                Query = q,
                HasSearched = !string.IsNullOrWhiteSpace(q),
                Results = string.IsNullOrWhiteSpace(q) ? new List<PatientSearchItem>() : MockSeed.Patients()
            };
            return View(model);
        }

        // R3 — Render (host de isla Vue)
        public ActionResult Load(long patientId, long questionnaireId)
        {
            ViewBag.Title = "Carga de ficha";
            ViewBag.PageId = "encounters";
            ViewBag.PatientId = patientId;
            ViewBag.PatientName = "García, Ana (HC 12345)";
            ViewBag.QuestionnaireId = questionnaireId;
            ViewBag.QuestionnaireTitle = "Cardio basal";
            ViewBag.Version = 3;
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Pacientes", Url.Action("Search")),
                System.Tuple.Create("Carga de ficha", (string)null)
            };
            return View();
        }

        // R6 — Lectura
        public ActionResult View(long id)
        {
            ViewBag.Title = "Ficha (lectura)";
            ViewBag.PageId = "encounters";
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Pacientes", Url.Action("Search")),
                System.Tuple.Create("Ficha #" + id, (string)null)
            };
            return View(MockSeed.EncounterReadonly(id));
        }

        // R7 — Timeline
        public ActionResult Timeline(long patientId)
        {
            ViewBag.Title = "Historial del paciente";
            ViewBag.PageId = "encounters";
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Pacientes", Url.Action("Search")),
                System.Tuple.Create("Historial", (string)null)
            };
            return View(MockSeed.PatientTimeline(patientId));
        }
    }
}
