using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using ChequeAR.Api.Auth;
using ChequeAR.Api.Models;
using ChequeAR.Application.Exceptions;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace ChequeAR.Api.Controllers.Mvc
{
    /// <summary>
    /// ABM del catálogo de variables clínicas: la única pantalla que separa al Administrador
    /// del Jefe. Los dos arman cuestionarios igual; sólo el Administrador define con qué.
    ///
    /// El permiso va a nivel clase, como en UsersController: no hay ninguna acción acá que un
    /// Jefe deba poder abrir, ni siquiera de lectura — para elegir variables usa el selector
    /// del editor, que consume api/variables con permiso de lectura.
    /// </summary>
    [RequiresPermission(Permissions.VariableManage)]
    public class VariablesController : Controller
    {
        private readonly ClinicalVariableService _variables;

        public VariablesController(ClinicalVariableService variables) { _variables = variables; }

        private static readonly JsonSerializerSettings CamelCase = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        public async Task<ActionResult> Index(string q = null, string type = null, bool includeInactive = false)
        {
            ViewBag.Title = "Variables";
            ViewBag.PageId = "variables";
            ViewBag.Breadcrumbs = new List<Tuple<string, string>>
            {
                Tuple.Create("Inicio", Url.Action("Index", "Home")),
                Tuple.Create("Variables", (string)null)
            };

            var model = new VariableListViewModel
            {
                SearchTerm = q,
                TypeFilter = type,
                IncludeInactive = includeInactive
            };

            try
            {
                QuestionType? tipo = string.IsNullOrWhiteSpace(type) ? (QuestionType?)null : QuestionTypeMap.FromUi(type);
                var pagina = await _variables.ListAsync(q, tipo, includeInactive, 0, 200);
                foreach (var v in pagina.Items)
                {
                    model.Variables.Add(new VariableListItem
                    {
                        Id = v.Id,
                        Code = v.Code,
                        Name = v.Name,
                        TypeLabel = TypeLabel(v.Type),
                        Unit = v.Unit,
                        Range = Range(v.MinValue, v.MaxValue),
                        SnomedCode = v.SnomedCode,
                        SnomedDisplay = v.SnomedDisplay,
                        IsActive = v.IsActive
                    });
                }
            }
            catch (Exception)
            {
                model.Variables.Clear();
                ViewBag.DataError = "Ocurrió un error al procesar la solicitud.";
            }

            return View(model);
        }

        /// <summary>id == null => variable nueva.</summary>
        public async Task<ActionResult> Details(Guid? id)
        {
            ViewBag.Title = id.HasValue ? "Editar variable" : "Nueva variable";
            ViewBag.PageId = "variables";
            ViewBag.Breadcrumbs = new List<Tuple<string, string>>
            {
                Tuple.Create("Inicio", Url.Action("Index", "Home")),
                Tuple.Create("Variables", Url.Action("Index")),
                Tuple.Create(id.HasValue ? "Editar" : "Nueva", (string)null)
            };

            ClinicalVariableDto dto;
            if (id.HasValue)
            {
                var v = await _variables.GetAsync(id.Value);
                if (v == null) return HttpNotFound();
                dto = ClinicalVariableDtoMap.ToDto(v);
                var uso = await _variables.GetUsageAsync(id.Value);
                ViewBag.UsageCount = uso.QuestionCount;
            }
            else
            {
                // Los defaults del alta: lo más común es una medición numérica.
                dto = new ClinicalVariableDto { Type = "decimal", IsActive = true };
            }

            // Se serializa en el controller y no en la vista, como hace
            // QuestionnairesController.Edit con los props del editor.
            ViewBag.VariableProps = JsonConvert.SerializeObject(dto, CamelCase);
            return View(new VariableFormViewModel { Id = id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<ActionResult> Save(VariableFormViewModel model)
        {
            try
            {
                var dto = JsonConvert.DeserializeObject<ClinicalVariableDto>(model.PayloadJson ?? "");
                if (dto == null)
                {
                    TempData["Toast"] = "No se recibió ninguna variable para guardar.";
                    return RedirectToAction("Index");
                }

                var entidad = ClinicalVariableDtoMap.ToEntity(dto);
                if (model.Id.HasValue)
                {
                    var actualizada = await _variables.UpdateAsync(model.Id.Value, entidad, User.Identity.Name);
                    if (actualizada == null) return HttpNotFound();
                    TempData["Toast"] = "Variable actualizada.";
                }
                else
                {
                    await _variables.CreateAsync(entidad, User.Identity.Name);
                    TempData["Toast"] = "Variable creada.";
                }
            }
            catch (ValidationFailedException ex)
            {
                // Una regla por línea: el admin ve todo lo que tiene que corregir de una.
                TempData["Toast"] = string.Join(" ", ex.Errors);
                return RedirectToAction("Details", new { id = model.Id });
            }
            catch (JsonException)
            {
                TempData["Toast"] = "No se pudieron leer los datos del formulario.";
                return RedirectToAction("Details", new { id = model.Id });
            }
            catch (Exception)
            {
                TempData["Toast"] = "No se pudieron guardar los cambios.";
                return RedirectToAction("Details", new { id = model.Id });
            }

            return RedirectToAction("Index");
        }

        /// <summary>
        /// Baja. El servicio decide si borra o desactiva según esté en uso; el toast dice cuál
        /// de las dos pasó, porque no es lo mismo y el admin tiene que saberlo.
        /// </summary>
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<ActionResult> Delete(Guid id)
        {
            try
            {
                var outcome = await _variables.DeleteAsync(id, User.Identity.Name);
                TempData["Toast"] = outcome == VariableDeleteOutcome.DeactivatedBecauseInUse
                    ? "La variable está en uso en algún cuestionario: se desactivó en vez de eliminarse."
                    : "Variable eliminada.";
            }
            catch (Exception)
            {
                TempData["Toast"] = "No se pudo dar de baja la variable.";
            }
            return RedirectToAction("Index");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<ActionResult> Activate(Guid id)
        {
            try
            {
                await _variables.SetActiveAsync(id, true, User.Identity.Name);
                TempData["Toast"] = "Variable reactivada.";
            }
            catch (Exception)
            {
                TempData["Toast"] = "No se pudo reactivar la variable.";
            }
            return RedirectToAction("Index");
        }

        private static string TypeLabel(QuestionType t)
        {
            switch (t)
            {
                case QuestionType.String: return "Texto corto";
                case QuestionType.Text: return "Texto largo";
                case QuestionType.Integer: return "Número entero";
                case QuestionType.Decimal: return "Número decimal";
                case QuestionType.Boolean: return "Sí / No";
                case QuestionType.Choice: return "Opción única";
                case QuestionType.OpenChoice: return "Opción múltiple";
                case QuestionType.Date: return "Fecha";
                case QuestionType.DateTime: return "Fecha y hora";
                case QuestionType.Attachment: return "Adjunto";
                case QuestionType.Scale: return "Escala";
                default: return t.ToString();
            }
        }

        private static string Range(decimal? min, decimal? max)
        {
            if (!min.HasValue && !max.HasValue) return null;
            var lo = min.HasValue ? min.Value.ToString(CultureInfo.InvariantCulture) : "−∞";
            var hi = max.HasValue ? max.Value.ToString(CultureInfo.InvariantCulture) : "∞";
            return lo + " – " + hi;
        }
    }
}
