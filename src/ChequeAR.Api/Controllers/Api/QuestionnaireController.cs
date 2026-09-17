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

using ChequeAR.Application.Fhir;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Api.Controllers.Api
{
    [RoutePrefix("fhir/r5/Questionnaire")]
    public class QuestionnaireController : ApiController
    {
        
        private readonly QuestionnaireService _service;

        public QuestionnaireController(QuestionnaireService service)
        {
            _service = service;
        }

        [HttpGet, Route(""), Authorize]
        public async Task<IHttpActionResult> Search([FromUri] string status = null, [FromUri(Name = "title:contains")] string titleContains = null, [FromUri] string specialty = null, [FromUri] int _count = 50, [FromUri] int _offset = 0)
        {
            QuestionnaireStatus? st = null;
            if (Enum.TryParse(status, true, out QuestionnaireStatus parsed)) st = parsed;
            try
            {
                var items = await _service.SearchAsync(st, titleContains, specialty, _offset, _count);

                var total = await _service.CountAsync(st, titleContains, specialty);
                var recursos = items.Select(q => (Hl7.Fhir.Model.Resource)ToFhir(q));
                return Ok(FhirBundles.SearchSet(recursos, total, FhirBaseUrl()));
            }
            catch (SpecialtyForbiddenException)
            {
                // El filtro ?specialty= pidió algo fuera del scope del usuario: se rechaza,
                // no se devuelve un Bundle vacío (eso le mentiría "no hay nada").
                return Content(HttpStatusCode.Forbidden, OperationOutcomes.Forbidden(
                    "No tenés acceso a esa especialidad."));
            }
        }

        [HttpGet, Route("{id:guid}"), Authorize]
        public async Task<IHttpActionResult> Get(Guid id)
        {
            Domain.Entities.Questionnaire q;
            try
            {
                q = await _service.GetAsync(id);
            }
            catch (SpecialtyForbiddenException)
            {
                // No confirmar existencia de un recurso de otra especialidad: mismo 404
                // que si no existiera.
                return Content(HttpStatusCode.NotFound, OperationOutcomes.NotFound());
            }
            if (q == null) return Content(HttpStatusCode.NotFound, OperationOutcomes.NotFound());

            // ETag = versión del registro. El cliente la devuelve en If-Match al escribir.
            // Viaja además en meta.versionId, que es el mecanismo nativo del estándar.
            var response = Request.CreateResponse(HttpStatusCode.OK, ToFhir(q));
            response.Headers.ETag = ETagFor(q);
            return ResponseMessage(response);
        }

        /// <summary>
        /// El número de versión publicada que se declara en el recurso. 0 = nunca se publicó;
        /// es correcto que un borrador diga 0 y no invente una versión que no existe.
        /// </summary>
        private static Hl7.Fhir.Model.Questionnaire ToFhir(Questionnaire q)
            => FhirQuestionnaireBuilder.ToResource(q, q.Versions?.Count ?? 0);

        /// <summary>Base del servidor FHIR, para el fullUrl de cada entrada del Bundle.</summary>
        private string FhirBaseUrl()
            => Request.RequestUri.GetLeftPart(UriPartial.Authority) + "/fhir/r5";

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
        public async Task<IHttpActionResult> Create([FromBody] Hl7.Fhir.Model.Questionnaire body)
        {
            var q = FhirQuestionnaireParser.ToDomain(body);
            if (q == null) return Content(HttpStatusCode.BadRequest, OperationOutcomes.Invalid());

            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            try
            {
                var created = await _service.CreateAsync(q, userId);
                var response = Request.CreateResponse(HttpStatusCode.Created, ToFhir(created));
                response.Headers.ETag = ETagFor(created);
                return ResponseMessage(response);
            }
            catch (SpecialtyForbiddenException)
            {
                return Content(HttpStatusCode.Forbidden, OperationOutcomes.Forbidden(
                    "No tenés acceso a esa especialidad."));
            }
            catch (ValidationFailedException ex)
            {
                return Content((HttpStatusCode)422, OperationOutcomes.ForValidationErrors(ex.Errors));
            }
        }

        [HttpPut, Route("{id:guid}"), Authorize(Roles = Role.Administrador + "," + Role.Jefe)]
        public async Task<IHttpActionResult> Update(Guid id, [FromBody] Hl7.Fhir.Model.Questionnaire body)
        {
            var patch = FhirQuestionnaireParser.ToDomain(body);
            if (patch == null) return Content(HttpStatusCode.BadRequest, OperationOutcomes.Invalid());

            // If-Match obligatorio: sin él no hay forma de saber si el cliente vio la última
            // versión, y una edición concurrente pisaría el trabajo ajeno en silencio.
            // 428 Precondition Required (RFC 6585) existe justamente para este caso.
            var expectedVersion = ParseIfMatch(Request);
            if (!expectedVersion.HasValue)
                return Content((HttpStatusCode)428, OperationOutcomes.PreconditionRequired());

            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            try
            {
                var updated = await _service.UpdateAsync(id, patch, userId, expectedVersion);
                var response = Request.CreateResponse(HttpStatusCode.OK, ToFhir(updated));
                response.Headers.ETag = ETagFor(updated);
                return ResponseMessage(response);
            }
            catch (ConcurrencyConflictException ex)
            {
                return Content(HttpStatusCode.Conflict,
                    OperationOutcomes.Conflict(ex.ExpectedVersion, ex.ActualVersion));
            }
            catch (SpecialtyForbiddenException)
            {
                return Content(HttpStatusCode.Forbidden, OperationOutcomes.Forbidden(
                    "No tenés acceso a esa especialidad."));
            }
            catch (ValidationFailedException ex)
            {
                // Especialidad inexistente/inactiva al mover el cuestionario: 422, no 500.
                return Content((HttpStatusCode)422, OperationOutcomes.ForValidationErrors(ex.Errors));
            }
            catch (InvalidOperationException ex)
            {
                // Mismo criterio que Publish: "no existe" y "no se puede editar en este
                // estado" (ej. archivado) son ambos errores del cliente, no del server.
                return Content((HttpStatusCode)422, OperationOutcomes.Unprocessable(ex.Message));
            }
        }

        [HttpPost, Route("{id:guid}/$publish"), Authorize(Roles = Role.Administrador + "," + Role.Jefe)]
        public async Task<IHttpActionResult> Publish(Guid id, [FromBody] PublishBody body)
        {
            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            try
            {
                var q = await _service.PublishAsync(id, userId, body?.ChangeNote);
                return Ok(ToFhir(q));
            }
            catch (SpecialtyForbiddenException)
            {
                return Content(HttpStatusCode.Forbidden, OperationOutcomes.Forbidden(
                    "No tenés acceso a esa especialidad."));
            }
            catch (ValidationFailedException ex)
            {
                // UN issue por regla incumplida. Con un mensaje concatenado, quien publica
                // arregla la primera regla, vuelve a intentar y descubre la segunda.
                return Content((HttpStatusCode)422, OperationOutcomes.ForValidationErrors(ex.Errors));
            }
            catch (InvalidOperationException ex)
            {
                return Content((HttpStatusCode)422, OperationOutcomes.Unprocessable(ex.Message));
            }
        }

        [HttpPost, Route("{id:guid}/$archive"), Authorize(Roles = Role.Administrador + "," + Role.Jefe)]
        public async Task<IHttpActionResult> Archive(Guid id)
        {
            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            try
            {
                await _service.ArchiveAsync(id, userId);
                return Ok();
            }
            catch (SpecialtyForbiddenException)
            {
                return Content(HttpStatusCode.Forbidden, OperationOutcomes.Forbidden(
                    "No tenés acceso a esa especialidad."));
            }
            catch (InvalidOperationException ex)
            {
                return Content((HttpStatusCode)422, OperationOutcomes.Unprocessable(ex.Message));
            }
        }

        [HttpPost, Route("{id:guid}/$clone"), Authorize(Roles = Role.Administrador + "," + Role.Jefe)]
        public async Task<IHttpActionResult> Clone(Guid id, [FromBody] CloneBody body)
        {
            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            try
            {
                var copy = await _service.CloneAsync(id, body?.Title, userId);
                var response = Request.CreateResponse(HttpStatusCode.Created, ToFhir(copy));
                response.Headers.ETag = ETagFor(copy);
                return ResponseMessage(response);
            }
            catch (SpecialtyForbiddenException)
            {
                return Content(HttpStatusCode.Forbidden, OperationOutcomes.Forbidden(
                    "No tenés acceso a esa especialidad."));
            }
            catch (InvalidOperationException)
            {
                return Content(HttpStatusCode.NotFound, OperationOutcomes.NotFound());
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
                    return Ok(OperationOutcomes.Informational(
                        "El cuestionario tiene respuestas cargadas: se archivó en vez de eliminarse."));
                }
                return StatusCode(HttpStatusCode.NoContent);
            }
            catch (SpecialtyForbiddenException)
            {
                // Delete es sólo-Admin (siempre Unrestricted): esto no debería poder
                // ocurrir, pero se deja como defensa en profundidad.
                return Content(HttpStatusCode.Forbidden, OperationOutcomes.Forbidden(
                    "No tenés acceso a esa especialidad."));
            }
            catch (InvalidOperationException)
            {
                return Content(HttpStatusCode.NotFound, OperationOutcomes.NotFound());
            }
        }

        public class PublishBody { public string ChangeNote { get; set; } }
        public class CloneBody { public string Title { get; set; } }
    }
}
