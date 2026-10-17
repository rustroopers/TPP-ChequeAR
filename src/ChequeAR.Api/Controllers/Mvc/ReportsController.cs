using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using ChequeAR.Api.Auth;
using ChequeAR.Api.Models;
using ChequeAR.Application.Dtos;
using ChequeAR.Application.Services;
using ChequeAR.Application.Reports;
using ChequeAR.Domain.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace ChequeAR.Api.Controllers.Mvc
{
    // Cierra el acceso de Medico: reportes son Admin/Jefe (ver RolePolicy).
    [RequiresPermission(Permissions.ReportRead)]
    public class ReportsController : Controller
    {
        private readonly SavedReportService _saved;
        private readonly ReportExecutor _executor;
        private readonly QuestionnaireService _questionnaires;

        public ReportsController(SavedReportService saved, ReportExecutor executor, QuestionnaireService questionnaires)
        {
            _saved = saved;
            _executor = executor;
            _questionnaires = questionnaires;
        }

        // E1
        public async Task<ActionResult> Index()
        {
            ViewBag.Title = "Reportes";
            ViewBag.PageId = "reports";
            var model = new ReportListViewModel();
            try
            {
                // Misma identidad que estampa SessionToken al emitir el bearer, que es con la
                // que la API guarda los reportes: si difirieran, el listado saldría vacío.
                var userId = User.Identity.Name;
                // El scope de especialidad sale de ISpecialtyGuard adentro del servicio, no
                // de acá: antes este controller mandaba `new string[0]`, así que un Jefe
                // nunca veía los reportes que sus colegas de la misma especialidad
                // compartieron con Scope="specialty" -- sólo los propios.
                var saved = await _saved.ListForUserAsync(userId);
                // El titulo del cuestionario base, no su GUID. Una sola consulta liviana (id y
                // titulo) en vez de cargar cada cuestionario entero, con todas sus preguntas.
                var titles = (await _questionnaires.ListTitlesAsync())
                    .Where(q => !q.IsDeleted)
                    .ToDictionary(q => q.Id, q => q.Title);
                foreach (var r in saved)
                {
                    model.Items.Add(new SavedReportItem
                    {
                        Id = r.Id, Name = r.Name,
                        QuestionnaireTitle = titles.TryGetValue(r.QuestionnaireId, out var title)
                            ? title : "(cuestionario eliminado)",
                        Author = r.CreatedBy,
                        LastUsedAt = ChequeAR.Api.Helpers.ArgentinaTime.FormatDate(r.UpdatedAt)
                    });
                }
            }
            catch (Exception ex)
            {
                Helpers.HandledErrors.Log(this, ex);
                model.Items.Clear();
                ViewBag.DataError = "Ocurrió un error al procesar la solicitud.";
            }
            return View(model);
        }

        // E2 host
        public ActionResult Build(Guid? id = null)
        {
            ViewBag.Title = id.HasValue ? "Editar reporte" : "Nuevo reporte";
            ViewBag.PageId = "reports";
            ViewBag.ReportId = id;
            return View();
        }

        // E3
        public async Task<ActionResult> Preview(Guid id, int? page = null)
        {
            ViewBag.Title = "Vista previa";
            ViewBag.PageId = "reports";
            ViewBag.ReportId = id;

            var model = new ReportPreviewViewModel();
            try
            {
                var resultado = await _saved.GetForUserAsync(id, User.Identity.Name);
                if (resultado.Outcome != SavedReportOutcome.Ok) return HttpNotFound();
                var saved = resultado.Report;

                model.ReportName = saved.Name;

                var query = JsonConvert.DeserializeObject<ReportQuery>(saved.QueryJson ?? "{}")
                            ?? new ReportQuery();
                query.QuestionnaireId = saved.QuestionnaireId;
                if (page.HasValue && page.Value > 0) query.Page = page.Value;

                model.Page = query.Page;
                model.PageSize = query.PageSize;

                // Formato ancho (una fila por ficha), igual que /api/reports/query. Antes esta
                // pantalla usaba el resultado crudo y mostraba el formato largo: una fila por
                // respuesta-pregunta, con columnas ResponseId/LinkId/ValueDecimal, y un Total
                // inflado por el numero de preguntas.
                var result = await _executor.ExecuteAsync(query);
                model.Total = result.Total;

                if (result.Rows.Count > 0)
                    model.Columns = result.Rows[0].Keys.ToList();

                foreach (var row in result.Rows)
                {
                    var previewRow = new ReportPreviewRow();
                    foreach (var col in model.Columns)
                    {
                        row.TryGetValue(col, out var value);
                        previewRow.Cells[col] = value?.ToString() ?? "";
                    }
                    model.Rows.Add(previewRow);
                }
            }
            catch (Exception ex)
            {
                Helpers.HandledErrors.Log(this, ex);
                ViewBag.DataError = "Ocurrió un error al procesar la solicitud.";
            }
            return View(model);
        }

        // E5
        public async Task<ActionResult> Stats(Guid? id)
        {
            ViewBag.Title = "Estadísticas";
            ViewBag.PageId = "reports";
            ViewBag.ReportId = id;

            // Sin reporte se muestra el estado vacío (antes, un 500 por el id requerido).
            if (!id.HasValue) return View(new ReportStatsViewModel());

            var model = new ReportStatsViewModel { ReportId = id.Value };
            try
            {
                var resultado = await _saved.GetForUserAsync(id.Value, User.Identity.Name);
                if (resultado.Outcome != SavedReportOutcome.Ok) return HttpNotFound();
                var saved = resultado.Report;
                model.ReportName = saved.Name;

                // La query viaja a la isla tal cual, y la isla se la manda a /api/reports/stats:
                // los numeros salen de los mismos filtros que la tabla, no de un id suelto.
                var query = JsonConvert.DeserializeObject<ReportQuery>(saved.QueryJson ?? "{}") ?? new ReportQuery();
                query.QuestionnaireId = saved.QuestionnaireId;
                model.QueryJson = JsonConvert.SerializeObject(query, new JsonSerializerSettings
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver(),
                    NullValueHandling = NullValueHandling.Ignore
                });
            }
            catch (Exception ex)
            {
                Helpers.HandledErrors.Log(this, ex);
                ViewBag.DataError = "Ocurrió un error al procesar la solicitud.";
            }
            return View(model);
        }
    }
}
