using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using ChequeAR.Api.Auth;
using ChequeAR.Application.Exceptions;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Api.Controllers.Api
{
    [RoutePrefix("fhir/r5/Questionnaire")]
    public class QuestionnaireController : ApiController
    {
        private readonly IQuestionnaireRepository _repo;
        private readonly QuestionnaireService _service;

        public QuestionnaireController(IQuestionnaireRepository repo, QuestionnaireService service)
        {
            _repo = repo;
            _service = service;
        }

        [HttpGet, Route(""), Authorize]
        public async Task<IHttpActionResult> Search([FromUri] string status = null, [FromUri(Name = "title:contains")] string titleContains = null, [FromUri] string specialty = null, [FromUri] int _count = 50, [FromUri] int _offset = 0)
        {
            QuestionnaireStatus? st = null;
            if (Enum.TryParse(status, true, out QuestionnaireStatus parsed)) st = parsed;
            var items = await _repo.SearchAsync(st, titleContains, specialty, _offset, _count);
            return Ok(new { resourceType = "Bundle", type = "searchset", total = items.Count, entry = items });
        }

        [HttpGet, Route("{id:guid}"), Authorize]
        public async Task<IHttpActionResult> Get(Guid id)
        {
            var q = await _repo.GetAsync(id);
            if (q == null) return NotFound();

            // ETag = versión del registro. El cliente la devuelve en If-Match al escribir.
            var response = Request.CreateResponse(HttpStatusCode.OK, q);
            response.Headers.ETag = ETagFor(q);
            return ResponseMessage(response);
        }

        private static EntityTagHeaderValue ETagFor(Questionnaire q)
        {
            // Weak ETag: identifica la versión del recurso, no una representación byte a byte.
            return new EntityTagHeaderValue("\"" + q.ConcurrencyVersion + "\"", true);
        }

        // Acepta W/"3" y "3". Devuelve null si no hay un número adentro.
        private static int? ParseIfMatch(HttpRequestMessage request)
        {
            var tag = request.Headers.IfMatch?.FirstOrDefault();
            if (tag == null) return null;
            var raw = (tag.Tag ?? string.Empty).Trim('"');
            return int.TryParse(raw, out var version) ? version : (int?)null;
        }

        [HttpPost, Route(""), Authorize(Roles = Role.Administrador + "," + Role.Jefe)]
        public async Task<IHttpActionResult> Create([FromBody] Questionnaire q)
        {
            if (q == null) return BadRequest("body required");
            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            var created = await _service.CreateAsync(q, userId);
            var response = Request.CreateResponse(HttpStatusCode.Created, created);
            response.Headers.ETag = ETagFor(created);
            return ResponseMessage(response);
        }

        [HttpPut, Route("{id:guid}"), Authorize(Roles = Role.Administrador + "," + Role.Jefe)]
        public async Task<IHttpActionResult> Update(Guid id, [FromBody] Questionnaire patch)
        {
            if (patch == null) return BadRequest("body required");

            // If-Match obligatorio: sin él no hay forma de saber si el cliente vio la última
            // versión, y una edición concurrente pisaría el trabajo ajeno en silencio.
            // 428 Precondition Required (RFC 6585) existe justamente para este caso.
            var expectedVersion = ParseIfMatch(Request);
            if (!expectedVersion.HasValue)
            {
                return Content((HttpStatusCode)428, new
                {
                    error = "Falta el header If-Match con la versión del cuestionario (ej: If-Match: W/\"3\")."
                });
            }

            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            try
            {
                var updated = await _service.UpdateAsync(id, patch, userId, expectedVersion);
                var response = Request.CreateResponse(HttpStatusCode.OK, updated);
                response.Headers.ETag = ETagFor(updated);
                return ResponseMessage(response);
            }
            catch (ConcurrencyConflictException ex)
            {
                return Content(HttpStatusCode.Conflict, new
                {
                    error = ex.Message,
                    expectedVersion = ex.ExpectedVersion,
                    actualVersion = ex.ActualVersion
                });
            }
            catch (InvalidOperationException ex)
            {
                // Mismo criterio que Publish: "no existe" y "no se puede editar en este
                // estado" (ej. archivado) son ambos errores del cliente, no del server.
                return Content((HttpStatusCode)422, new { error = ex.Message });
            }
        }

        [HttpPost, Route("{id:guid}/$publish"), Authorize(Roles = Role.Administrador + "," + Role.Jefe)]
        public async Task<IHttpActionResult> Publish(Guid id, [FromBody] PublishBody body)
        {
            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            try
            {
                var q = await _service.PublishAsync(id, body?.FhirJson, userId, body?.ChangeNote);
                return Ok(q);
            }
            catch (InvalidOperationException ex)
            {
                return Content((HttpStatusCode)422, new { error = ex.Message });
            }
        }

        [HttpPost, Route("{id:guid}/$archive"), Authorize(Roles = Role.Administrador + "," + Role.Jefe)]
        public async Task<IHttpActionResult> Archive(Guid id)
        {
            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            await _service.ArchiveAsync(id, userId);
            return Ok();
        }

        [HttpPost, Route("{id:guid}/$clone"), Authorize(Roles = Role.Administrador + "," + Role.Jefe)]
        public async Task<IHttpActionResult> Clone(Guid id, [FromBody] CloneBody body)
        {
            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            try
            {
                var copy = await _service.CloneAsync(id, body?.Title, userId);
                var response = Request.CreateResponse(HttpStatusCode.Created, copy);
                response.Headers.ETag = ETagFor(copy);
                return ResponseMessage(response);
            }
            catch (InvalidOperationException)
            {
                return NotFound();
            }
        }

        [HttpDelete, Route("{id:guid}"), Authorize(Roles = Role.Administrador)]
        public async Task<IHttpActionResult> Delete(Guid id)
        {
            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            try
            {
                var outcome = await _service.DeleteAsync(id, userId);
                // Un 204 acá sería mentira: el recurso sigue existiendo, archivado.
                if (outcome == DeleteOutcome.ArchivedBecauseInUse)
                {
                    return Ok(new
                    {
                        outcome = "archived",
                        message = "El cuestionario tiene respuestas cargadas: se archivó en vez de eliminarse."
                    });
                }
                return StatusCode(HttpStatusCode.NoContent);
            }
            catch (InvalidOperationException)
            {
                return NotFound();
            }
        }

        public class PublishBody { public string FhirJson { get; set; } public string ChangeNote { get; set; } }
        public class CloneBody { public string Title { get; set; } }
    }
}
