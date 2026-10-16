using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using ChequeAR.Api.Auth;
using ChequeAR.Api.Models;
using ChequeAR.Application.Exceptions;
using ChequeAR.Application.Fhir;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Api.Controllers.Api
{
    /// <summary>
    /// Catálogo de variables clínicas.
    ///
    /// API interna (api/variables) y no un recurso FHIR: el prefijo fhir/r5 está reservado para
    /// los recursos que ChequeAR sí expone como tales, y una variable no es uno — el análogo del
    /// estándar (ObservationDefinition) no lo consume ningún cliente de este sistema. Los
    /// catálogos ya viven acá: api/users, api/specialties.
    ///
    /// Asimetría de permisos deliberada: leer pide QuestionnaireRead (el Jefe necesita el
    /// catálogo para armar cuestionarios), escribir pide VariableManage (sólo Administrador).
    /// </summary>
    [RoutePrefix("api/variables")]
    public class ClinicalVariableController : ApiController
    {
        private readonly ClinicalVariableService _service;

        public ClinicalVariableController(ClinicalVariableService service) { _service = service; }

        private string UserId => CurrentUser.Id(User as ClaimsPrincipal);

        /// <param name="published">
        /// Sólo las publicadas, cada una con la definición de su versión vigente y no la del
        /// borrador: es lo que usa el buscador del editor de cuestionarios.
        /// </param>
        [HttpGet, Route(""), RequiresPermissionApi(Permissions.QuestionnaireRead)]
        public async Task<IHttpActionResult> List(
            [FromUri] string q = null, [FromUri] string type = null,
            [FromUri] bool includeInactive = false, [FromUri] bool published = false,
            [FromUri] int _count = 50, [FromUri] int _offset = 0)
        {
            // type vacío = sin filtro; un tipo desconocido es un 422 (QuestionTypeMap.FromUi).
            QuestionType? tipo = string.IsNullOrWhiteSpace(type) ? (QuestionType?)null : QuestionTypeMap.FromUi(type);
            var pagina = await _service.ListAsync(q, tipo, includeInactive ? (bool?)null : true,
                System.Math.Max(_offset, 0), System.Math.Min(System.Math.Max(_count, 1), 100), published);

            if (published)
            {
                var vigentes = await _service.GetCurrentDefinitionsAsync(pagina.Items);
                return Ok(new
                {
                    total = pagina.Total,
                    results = pagina.Items.Where(v => vigentes.ContainsKey(v.Id))
                        .Select(v => ClinicalVariableDtoMap.ToDto(v, vigentes[v.Id]))
                });
            }

            var estado = await _service.GetPublicationAsync(pagina.Items);
            return Ok(new
            {
                total = pagina.Total,
                results = pagina.Items.Select(v => ClinicalVariableDtoMap.ToDto(v, estado[v.Id]))
            });
        }

        [HttpGet, Route("{id:guid}"), RequiresPermissionApi(Permissions.QuestionnaireRead)]
        public async Task<IHttpActionResult> Get(Guid id)
        {
            var v = await _service.GetAsync(id);
            if (v == null) return Content(HttpStatusCode.NotFound, OperationOutcomes.NotFound());
            var estado = await _service.GetPublicationAsync(new[] { v });
            var response = Request.CreateResponse(HttpStatusCode.OK, ClinicalVariableDtoMap.ToDto(v, estado[v.Id]));
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"" + v.Revision + "\"", true);
            return ResponseMessage(response);
        }

        [HttpGet, Route("{id:guid}/versions"), RequiresPermissionApi(Permissions.QuestionnaireRead)]
        public async Task<IHttpActionResult> Versions(Guid id)
        {
            var versions = await _service.ListVersionsAsync(id);
            return Ok(versions.Select(x => new
            {
                version = x.VersionNumber,
                publishedAt = x.PublishedAt,
                publishedBy = x.PublishedBy,
                note = x.ChangeNote
            }));
        }

        /// <summary>La definición de una versión, con la forma del catálogo: la usa el editor para actualizar una pregunta.</summary>
        [HttpGet, Route("{id:guid}/versions/{number:int}"), RequiresPermissionApi(Permissions.QuestionnaireRead)]
        public async Task<IHttpActionResult> Version(Guid id, int number)
        {
            var v = await _service.GetAsync(id);
            var definition = v == null ? null : await _service.GetVersionAsync(id, number);
            if (definition == null) return Content(HttpStatusCode.NotFound, OperationOutcomes.NotFound());
            return Ok(ClinicalVariableDtoMap.ToDto(v, new PublishedVariable { VersionNumber = number, Definition = definition }));
        }

        /// <summary>Publica el borrador. If-Match (la revisión que vio quien publica) es opcional.</summary>
        [HttpPost, Route("{id:guid}/$publish"), RequiresPermissionApi(Permissions.VariableManage)]
        public async Task<IHttpActionResult> Publish(Guid id, [FromBody] PublishVariableDto body)
        {
            var version = await _service.PublishAsync(id, UserId, body?.Note, ParseIfMatch(Request));
            if (version == null) return Content(HttpStatusCode.NotFound, OperationOutcomes.NotFound());
            return Ok(new { version = version.VersionNumber });
        }

        /// <summary>Dónde se usa. Es lo que el ABM muestra antes de ofrecer la baja.</summary>
        [HttpGet, Route("{id:guid}/usage"), RequiresPermissionApi(Permissions.QuestionnaireRead)]
        public async Task<IHttpActionResult> Usage(Guid id)
        {
            var uso = await _service.GetUsageAsync(id);
            return Ok(new { count = uso.QuestionCount, questionnaires = uso.QuestionnaireIds });
        }

        [HttpPost, Route(""), RequiresPermissionApi(Permissions.VariableManage)]
        public async Task<IHttpActionResult> Create([FromBody] ClinicalVariableDto body)
        {
            if (body == null) return Content(HttpStatusCode.BadRequest, OperationOutcomes.Invalid());
            var creada = await _service.CreateAsync(ClinicalVariableDtoMap.ToEntity(body), UserId);
            return Content(HttpStatusCode.Created, ClinicalVariableDtoMap.ToDto(creada));
        }

        [HttpPut, Route("{id:guid}"), RequiresPermissionApi(Permissions.VariableManage)]
        public async Task<IHttpActionResult> Update(Guid id, [FromBody] ClinicalVariableDto body)
        {
            if (body == null) return Content(HttpStatusCode.BadRequest, OperationOutcomes.Invalid());
            var actualizada = await _service.UpdateAsync(id, ClinicalVariableDtoMap.ToEntity(body), UserId, ParseIfMatch(Request));
            if (actualizada == null) return Content(HttpStatusCode.NotFound, OperationOutcomes.NotFound());
            var estado = await _service.GetPublicationAsync(new[] { actualizada });
            return Ok(ClinicalVariableDtoMap.ToDto(actualizada, estado[actualizada.Id]));
        }

        [HttpPost, Route("{id:guid}/activate"), RequiresPermissionApi(Permissions.VariableManage)]
        public Task<IHttpActionResult> Activate(Guid id) => SetActive(id, true);

        [HttpPost, Route("{id:guid}/deactivate"), RequiresPermissionApi(Permissions.VariableManage)]
        public Task<IHttpActionResult> Deactivate(Guid id) => SetActive(id, false);

        private async Task<IHttpActionResult> SetActive(Guid id, bool active)
        {
            var ok = await _service.SetActiveAsync(id, active, UserId);
            if (!ok) return Content(HttpStatusCode.NotFound, OperationOutcomes.NotFound());
            return StatusCode(HttpStatusCode.NoContent);
        }

        // Acepta W/"3" y "3".
        private static int? ParseIfMatch(HttpRequestMessage request)
        {
            var tag = request.Headers.IfMatch?.FirstOrDefault();
            if (tag == null) return null;
            return int.TryParse((tag.Tag ?? string.Empty).Trim('"'), out var revision) ? revision : (int?)null;
        }

        [HttpDelete, Route("{id:guid}"), RequiresPermissionApi(Permissions.VariableManage)]
        public async Task<IHttpActionResult> Delete(Guid id)
        {
            var outcome = await _service.DeleteAsync(id, UserId);
            // Mismo criterio que QuestionnaireController.Delete: un 204 acá sería mentira,
            // porque la variable sigue existiendo — desactivada para que no se use en nada nuevo.
            if (outcome == VariableDeleteOutcome.DeactivatedBecauseInUse)
            {
                return Ok(OperationOutcomes.Informational(
                    "La variable está en uso en al menos un cuestionario: se desactivó en vez de eliminarse."));
            }
            return StatusCode(HttpStatusCode.NoContent);
        }
    }
}
