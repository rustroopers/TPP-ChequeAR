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
                var sucesoras = await _variables.GetSuccessorsAsync(pagina.Items.Select(v => v.Id));
                var anteriores = new Dictionary<Guid, string>();
                foreach (var anteriorId in pagina.Items.Where(v => !v.IsPublished && v.ReplacesId.HasValue).Select(v => v.ReplacesId.Value).Distinct())
                    anteriores[anteriorId] = (await _variables.GetAsync(anteriorId))?.Name;
                model.Total = pagina.Total;
                foreach (var v in pagina.Items)
                {
                    sucesoras.TryGetValue(v.Id, out var sucesora);
                    string anterior = null;
                    if (!v.IsPublished && v.ReplacesId.HasValue) anteriores.TryGetValue(v.ReplacesId.Value, out anterior);
                    model.Variables.Add(new VariableListItem
                    {
                        Id = v.Id,
                        Code = v.Code,
                        Name = v.Name,
                        TypeLabel = QuestionTypeMap.Label(v.Type),
                        Unit = v.Unit,
                        Range = Range(v.MinValue, v.MaxValue),
                        SnomedCode = v.SnomedCode,
                        SnomedDisplay = v.SnomedDisplay,
                        IsActive = v.IsActive,
                        CreatedAt = ChequeAR.Api.Helpers.ArgentinaTime.FormatDate(v.CreatedAt),
                        IsPublished = v.IsPublished,
                        Revision = v.Revision,
                        ReplacedByName = sucesora?.Name,
                        ReplacesName = anterior
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

        /// <summary>id == null => variable nueva; con <paramref name="from"/>, a partir de esa.</summary>
        public async Task<ActionResult> Details(Guid? id, Guid? from = null)
        {
            ViewBag.Title = id.HasValue ? "Editar variable" : "Nueva variable";
            ViewBag.PageId = "variables";

            ClinicalVariableDto dto;
            if (id.HasValue)
            {
                var v = await _variables.GetAsync(id.Value);
                if (v == null) return HttpNotFound();
                dto = ClinicalVariableDtoMap.ToDto(v);
            }
            else if (from.HasValue)
            {
                try
                {
                    var draft = await _variables.DeriveAsync(from.Value);
                    if (draft == null) return HttpNotFound();
                    dto = ClinicalVariableDtoMap.ToDto(draft);
                    dto.IsActive = true;
                }
                catch (ValidationFailedException ex)
                {
                    TempData["Toast"] = string.Join(" ", ex.Errors);
                    TempData["ToastLevel"] = "error";
                    return RedirectToAction("Details", new { id = from });
                }
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
            if (dto.ReplacesId.HasValue)
            {
                dto.ReplacesName = (await _variables.GetAsync(dto.ReplacesId.Value))?.Name;
                ViewBag.VariableProps = JsonConvert.SerializeObject(dto, CamelCase);
            }
            var model = new VariableFormViewModel
            {
                Id = id,
                Name = dto.Name,
                IsActive = dto.IsActive,
                Revision = dto.Revision,
                IsPublished = dto.IsPublished,
                ReplacesId = dto.ReplacesId,
                ReplacesName = dto.ReplacesName
            };
            if (!id.HasValue) return model;
            if ((await _variables.GetSuccessorsAsync(new[] { id.Value })).TryGetValue(id.Value, out var sucesora))
            {
                model.ReplacedById = sucesora.Id;
                model.ReplacedByName = sucesora.Name;
            }
            return model;
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<ActionResult> Publish(Guid id, int? revision, string payloadJson, bool deactivateReplaced = false, string returnUrl = null)
        {
            try
            {
                var antes = await _variables.GetAsync(id);
                if (antes == null) return HttpNotFound();

                // Lo que está en el formulario se guarda primero: si no, se publicaría el borrador anterior.
                var dto = string.IsNullOrWhiteSpace(payloadJson) ? null : JsonConvert.DeserializeObject<ClinicalVariableDto>(payloadJson);
                if (dto != null)
                {
                    var guardada = await _variables.UpdateAsync(id, ClinicalVariableDtoMap.ToEntity(dto), User.Identity.Name, dto.Revision);
                    revision = guardada.Revision;
                }

                await _variables.PublishAsync(id, User.Identity.Name, revision, deactivateReplaced);
                var publicada = await _variables.GetAsync(id);
                var anterior = publicada.ReplacesId.HasValue ? await _variables.GetAsync(publicada.ReplacesId.Value) : null;
                TempData["Toast"] = anterior != null && !anterior.IsActive && publicada.DeactivateReplacedOnPublish
                    ? "Variable publicada. «" + anterior.Name + "» quedó dada de baja."
                    : "Variable publicada.";
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
            // Desde el listado se vuelve al listado, con sus filtros.
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) return Redirect(returnUrl);
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
                    TempData["Toast"] = "Cambios guardados.";
                    TempData["ToastLevel"] = "success";
                }
                else
                {
                    var creada = await _variables.CreateAsync(entidad, User.Identity.Name);
                    TempData["Toast"] = "Variable creada. Publíquela para poder usarla en los cuestionarios.";
                    TempData["ToastLevel"] = "success";
                    // La que reemplaza a otra se publica desde su pantalla: ahí se ve a cuál reemplaza.
                    if (creada.ReplacesId.HasValue) return RedirectToAction("Details", new { id = creada.Id });
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
                enviado.Id = guardada.Id;
                enviado.IsActive = guardada.IsActive;
                enviado.UpdatedAt = guardada.UpdatedAt;
                enviado.PublishedAt = guardada.PublishedAt;
            }
            else
            {
                enviado.Id = Guid.Empty;
                enviado.IsActive = true;
            }

            ViewBag.Title = model.Id.HasValue ? "Editar variable" : "Nueva variable";
            ViewBag.PageId = "variables";
            ViewBag.VariableProps = JsonConvert.SerializeObject(enviado, CamelCase);
            ViewBag.VariableUnsaved = true;
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
                    ? "Variable dada de baja."
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
            catch (ValidationFailedException ex)
            {
                TempData["Toast"] = string.Join(" ", ex.Errors);
                TempData["ToastLevel"] = "error";
            }
            catch (Exception ex)
            {
                Helpers.HandledErrors.Log(this, ex);
                TempData["Toast"] = "No se pudo reactivar la variable.";
                TempData["ToastLevel"] = "error";
            }
            return RedirectToAction("Index");
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
