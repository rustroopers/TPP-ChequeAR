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

        private const int PageSize = 20;

        public async Task<ActionResult> Index(string q = null, string type = null, string status = null, int page = 1)
        {
            ViewBag.Title = "Variables";
            ViewBag.PageId = "variables";

            var model = new VariableListViewModel
            {
                SearchTerm = q,
                TypeFilter = type,
                StatusFilter = status,
                Page = page < 1 ? 1 : page,
                PageSize = PageSize
            };

            // Mismo filtro de estado que el listado de cuestionarios: vacío = todas.
            bool? active = status == "active" ? true : status == "inactive" ? false : (bool?)null;

            try
            {
                // Un tipo desconocido en la URL se ignora: es un filtro, no un dato que se guarda.
                QuestionType? tipo = QuestionTypeMap.TryFromUi(type, out var t) ? t : (QuestionType?)null;
                var pagina = await _variables.ListAsync(q, tipo, active, (model.Page - 1) * PageSize, PageSize);
                var estado = await _variables.GetPublicationAsync(pagina.Items);
                model.Total = pagina.Total;
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
                        IsActive = v.IsActive,
                        CreatedAt = ChequeAR.Api.Helpers.ArgentinaTime.FormatDate(v.CreatedAt),
                        CurrentVersion = estado[v.Id].CurrentVersion,
                        HasUnpublishedChanges = estado[v.Id].CurrentVersion > 0 && estado[v.Id].HasUnpublishedChanges
                    });
                }
            }
            catch (Exception ex)
            {
                Helpers.HandledErrors.Log(this, ex);
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

            ClinicalVariableDto dto;
            if (id.HasValue)
            {
                var v = await _variables.GetAsync(id.Value);
                if (v == null) return HttpNotFound();
                var estado = await _variables.GetPublicationAsync(new[] { v });
                dto = ClinicalVariableDtoMap.ToDto(v, estado[v.Id]);
            }
            else
            {
                // Los defaults del alta: lo más común es una medición numérica.
                dto = new ClinicalVariableDto { Type = "decimal", IsActive = true };
            }

            // Se serializa en el controller y no en la vista, como hace
            // QuestionnairesController.Edit con los props del editor.
            ViewBag.VariableProps = JsonConvert.SerializeObject(dto, CamelCase);
            return View(await FormModelAsync(id, dto));
        }

        private async Task<VariableFormViewModel> FormModelAsync(Guid? id, ClinicalVariableDto dto)
        {
            var model = new VariableFormViewModel
            {
                Id = id,
                Revision = dto.Revision,
                CurrentVersion = dto.CurrentVersion,
                HasUnpublishedChanges = dto.CurrentVersion > 0 && dto.HasUnpublishedChanges
            };
            if (!id.HasValue) return model;
            foreach (var x in await _variables.ListVersionsAsync(id.Value))
            {
                model.Versions.Add(new VariableVersionItem
                {
                    Version = x.VersionNumber,
                    Date = ChequeAR.Api.Helpers.ArgentinaTime.FormatDateTime(x.PublishedAt),
                    Author = x.PublishedBy,
                    ChangeNote = x.ChangeNote,
                    IsCurrent = x.VersionNumber == dto.CurrentVersion
                });
            }
            return model;
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<ActionResult> Publish(Guid id, string changeNote, int? revision)
        {
            try
            {
                var antes = await _variables.GetAsync(id);
                if (antes == null) return HttpNotFound();
                var vigente = (await _variables.GetPublicationAsync(new[] { antes }))[id].CurrentVersion;
                var version = await _variables.PublishAsync(id, User.Identity.Name, changeNote, revision);
                TempData["Toast"] = version.VersionNumber == vigente
                    ? "No hay cambios para publicar."
                    : "Variable publicada (v" + version.VersionNumber + ").";
                TempData["ToastLevel"] = "success";
            }
            catch (ValidationFailedException ex)
            {
                TempData["Toast"] = string.Join(" ", ex.Errors);
                TempData["ToastLevel"] = "error";
            }
            catch (ConcurrencyConflictException ex)
            {
                TempData["Toast"] = ex.Message;
                TempData["ToastLevel"] = "error";
            }
            catch (Exception ex)
            {
                Helpers.HandledErrors.Log(this, ex);
                TempData["Toast"] = "No se pudo publicar la variable.";
                TempData["ToastLevel"] = "error";
            }
            return RedirectToAction("Details", new { id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<ActionResult> Save(VariableFormViewModel model)
        {
            ClinicalVariableDto dto = null;
            try
            {
                dto = JsonConvert.DeserializeObject<ClinicalVariableDto>(model.PayloadJson ?? "");
                if (dto == null)
                {
                    TempData["Toast"] = "No se recibió ninguna variable para guardar.";
                    TempData["ToastLevel"] = "error";
                    return RedirectToAction("Index");
                }

                var entidad = ClinicalVariableDtoMap.ToEntity(dto);
                if (model.Id.HasValue)
                {
                    var actualizada = await _variables.UpdateAsync(model.Id.Value, entidad, User.Identity.Name, dto.Revision);
                    if (actualizada == null) return HttpNotFound();
                    TempData["Toast"] = "Variable actualizada.";
                    TempData["ToastLevel"] = "success";
                }
                else
                {
                    await _variables.CreateAsync(entidad, User.Identity.Name);
                    TempData["Toast"] = "Variable creada.";
                    TempData["ToastLevel"] = "success";
                }
            }
            catch (ValidationFailedException ex)
            {
                // Una regla por línea: el admin ve todo lo que tiene que corregir de una.
                TempData["Toast"] = string.Join(" ", ex.Errors);
                TempData["ToastLevel"] = "error";
                return await RedisplayAsync(model, dto);
            }
            catch (ConcurrencyConflictException ex)
            {
                // Se recarga lo guardado: reenviar la revisión vieja pisaría al otro.
                TempData["Toast"] = ex.Message;
                TempData["ToastLevel"] = "error";
                return RedirectToAction("Details", new { id = model.Id });
            }
            catch (JsonException)
            {
                TempData["Toast"] = "No se pudieron leer los datos del formulario.";
                TempData["ToastLevel"] = "error";
                return RedirectToAction("Details", new { id = model.Id });
            }
            catch (Exception ex)
            {
                Helpers.HandledErrors.Log(this, ex);
                TempData["Toast"] = "No se pudieron guardar los cambios.";
                TempData["ToastLevel"] = "error";
                return RedirectToAction("Details", new { id = model.Id });
            }

            return RedirectToAction("Index");
        }

        /// <summary>
        /// Re-muestra lo enviado. La revisión es la que tenía el formulario, no la guardada: si
        /// otro guardó en el medio, el próximo envío tiene que chocar en vez de pisarlo.
        /// </summary>
        private async Task<ActionResult> RedisplayAsync(VariableFormViewModel model, ClinicalVariableDto enviado)
        {
            if (enviado == null) return RedirectToAction("Details", new { id = model.Id });

            if (model.Id.HasValue)
            {
                var guardada = await _variables.GetAsync(model.Id.Value);
                if (guardada == null) return HttpNotFound();
                var estado = await _variables.GetPublicationAsync(new[] { guardada });
                var original = ClinicalVariableDtoMap.ToDto(guardada, estado[guardada.Id]);
                enviado.Id = original.Id;
                enviado.IsActive = original.IsActive;
                enviado.UpdatedAt = original.UpdatedAt;
                enviado.CurrentVersion = original.CurrentVersion;
                enviado.HasUnpublishedChanges = original.HasUnpublishedChanges;
            }
            else
            {
                enviado.Id = Guid.Empty;
                enviado.IsActive = true;
            }

            ViewBag.Title = model.Id.HasValue ? "Editar variable" : "Nueva variable";
            ViewBag.PageId = "variables";
            ViewBag.VariableProps = JsonConvert.SerializeObject(enviado, CamelCase);
            return View("Details", await FormModelAsync(model.Id, enviado));
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
                TempData["ToastLevel"] = "success";
            }
            catch (Exception ex)
            {
                Helpers.HandledErrors.Log(this, ex);
                TempData["Toast"] = "No se pudo dar de baja la variable.";
                TempData["ToastLevel"] = "error";
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
                TempData["ToastLevel"] = "success";
            }
            catch (Exception ex)
            {
                Helpers.HandledErrors.Log(this, ex);
                TempData["Toast"] = "No se pudo reactivar la variable.";
                TempData["ToastLevel"] = "error";
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
