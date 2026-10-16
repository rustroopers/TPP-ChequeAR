using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using ChequeAR.Api.Fhir;
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

        [HttpGet, Route(""), RequiresPermissionApi(Permissions.QuestionnaireRead)]
        public async Task<IHttpActionResult> Search([FromUri] string status = null, [FromUri(Name = "title:contains")] string titleContains = null, [FromUri] string specialty = null, [FromUri] int _count = 50, [FromUri] int _offset = 0,
            [FromUri] string url = null, [FromUri] string version = null)
        {
            // GET recurso devuelve el borrador de trabajo; una versión publicada se lee por su
            // canonical: url + version (el número de versión publicada).
            if (!string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(version))
                return await SearchVersionAsync(url, version);

            QuestionnaireStatus? st = null;
            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!Enum.TryParse(status.Trim(), true, out QuestionnaireStatus parsed) || !Enum.IsDefined(typeof(QuestionnaireStatus), parsed))
                    return Content(HttpStatusCode.BadRequest, OperationOutcomes.Invalid("status no reconocido: " + status));
                st = parsed;
            }
            try
            {
                var items = await _service.SearchAsync(st, titleContains, specialty,
                    FhirBundles.ClampOffset(_offset), FhirBundles.ClampCount(_count));

                var total = await _service.CountAsync(st, titleContains, specialty);

                // Cada entrada del Bundle es el recurso completo. Armarlo desde lo que devuelve
                // la búsqueda (sin colecciones cargadas) disparaba una carga diferida por
                // colección y por fila: se trae la estructura de toda la página junta, y los
                // números de versión en una sola consulta.
                var ids = items.Select(q => q.Id).ToList();
                var completos = await _service.GetManyWithStructureAsync(ids);
                var versiones = await _service.GetVersionCountsAsync(ids);
                var recursos = completos.Select(q => (Hl7.Fhir.Model.Resource)FhirQuestionnaireBuilder.ToResource(
                    q, versiones.TryGetValue(q.Id, out var n) ? n : 0));
                return Ok(FhirBundles.SearchSet(recursos, total, Request.FhirBaseUrl(), Request.RequestUri,
                    FhirBundles.ClampOffset(_offset), FhirBundles.ClampCount(_count)));
            }
            catch (SpecialtyForbiddenException)
            {
                // El filtro ?specialty= pidió algo fuera del scope del usuario: se rechaza,
                // no se devuelve un Bundle vacío (eso le mentiría "no hay nada").
                return Content(HttpStatusCode.Forbidden, OperationOutcomes.Forbidden(
                    "No tenés acceso a esa especialidad."));
            }
        }

        private async Task<IHttpActionResult> SearchVersionAsync(string url, string version)
        {
            var recursos = new List<Hl7.Fhir.Model.Resource>();
            var ultimo = url.TrimEnd('/').Substring(url.TrimEnd('/').LastIndexOf('/') + 1);
            if (Guid.TryParse(ultimo, out var id) && int.TryParse(version, out var numero))
            {
                try
                {
                    var v = await _service.GetVersionAsync(id, numero);
                    if (v != null) recursos.Add(new Hl7.Fhir.Serialization.FhirJsonParser(
                        new Hl7.Fhir.Serialization.ParserSettings { PermissiveParsing = true, AcceptUnknownMembers = true, AllowUnrecognizedEnums = true })
                        .Parse<Hl7.Fhir.Model.Questionnaire>(v.FhirJson));
                }
                catch (SpecialtyForbiddenException) { /* fuera de scope: como si no existiera */ }
            }
            return Ok(FhirBundles.SearchSet(recursos, recursos.Count, Request.FhirBaseUrl()));
        }

        [HttpGet, Route("{id:guid}"), RequiresPermissionApi(Permissions.QuestionnaireRead)]
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

        private static EntityTagHeaderValue ETagFor(Questionnaire q)
        {
            return FhirHttp.WeakETag(q.ConcurrencyVersion);
        }

        [HttpPost, Route(""), RequiresPermissionApi(Permissions.QuestionnaireWrite)]
        public async Task<IHttpActionResult> Create([FromBody] Hl7.Fhir.Model.Questionnaire body)
        {
            if (!ModelState.IsValid) return Content(HttpStatusCode.BadRequest, OperationOutcomes.Invalid(FhirModelState.Describe(ModelState)));
            var q = FhirQuestionnaireParser.ToDomain(body);
            if (q == null) return Content(HttpStatusCode.BadRequest, OperationOutcomes.Invalid());
            EnsureSupported(body);

            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            var created = await _service.CreateAsync(q, userId);
            var response = Request.CreateResponse(HttpStatusCode.Created, ToFhir(created));
            response.Headers.Location = new Uri(Request.FhirBaseUrl() + "/Questionnaire/" + created.Id);
            response.Headers.ETag = ETagFor(created);
            return ResponseMessage(response);
        }

        [HttpPut, Route("{id:guid}"), RequiresPermissionApi(Permissions.QuestionnaireWrite)]
        public async Task<IHttpActionResult> Update(Guid id, [FromBody] Hl7.Fhir.Model.Questionnaire body)
        {
            if (!ModelState.IsValid) return Content(HttpStatusCode.BadRequest, OperationOutcomes.Invalid(FhirModelState.Describe(ModelState)));
            // FHIR: el id del cuerpo tiene que ser el de la URL.
            if (body != null && !string.IsNullOrEmpty(body.Id) && !string.Equals(body.Id, id.ToString(), StringComparison.OrdinalIgnoreCase))
                return Content(HttpStatusCode.BadRequest, OperationOutcomes.Invalid("El id del recurso no coincide con el de la URL."));
            var patch = FhirQuestionnaireParser.ToDomain(body);
            if (patch == null) return Content(HttpStatusCode.BadRequest, OperationOutcomes.Invalid());
            EnsureSupported(body);

            // If-Match obligatorio: sin él no hay forma de saber si el cliente vio la última
            // versión, y una edición concurrente pisaría el trabajo ajeno en silencio.
            // 428 Precondition Required (RFC 6585) existe justamente para este caso.
            var expectedVersion = Request.IfMatchVersion();
            if (!expectedVersion.HasValue)
                return Content((HttpStatusCode)428, OperationOutcomes.PreconditionRequired());

            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            var updated = await _service.UpdateAsync(id, patch, userId, expectedVersion);
            var response = Request.CreateResponse(HttpStatusCode.OK, ToFhir(updated));
            response.Headers.ETag = ETagFor(updated);
            return ResponseMessage(response);
        }

        private static void EnsureSupported(Hl7.Fhir.Model.Questionnaire body)
        {
            var errores = FhirQuestionnaireParser.UnsupportedItems(body);
            if (errores.Count > 0) throw new ValidationFailedException(errores);
        }

        [HttpPost, Route("{id:guid}/$publish"), RequiresPermissionApi(Permissions.QuestionnaireWrite)]
        public async Task<IHttpActionResult> Publish(Guid id, [FromBody] PublishBody body)
        {
            // Mismo contrato que el PUT: publicar congela lo que está en la base, así que hay
            // que demostrar que se vio la última versión del registro.
            var expectedVersion = Request.IfMatchVersion();
            if (!expectedVersion.HasValue)
                return Content((HttpStatusCode)428, OperationOutcomes.PreconditionRequired());

            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            var q = await _service.PublishAsync(id, userId, body?.ChangeNote, expectedVersion);
            var response = Request.CreateResponse(HttpStatusCode.OK, ToFhir(q));
            response.Headers.ETag = ETagFor(q);
            return ResponseMessage(response);
        }

        [HttpPost, Route("{id:guid}/$archive"), RequiresPermissionApi(Permissions.QuestionnaireWrite)]
        public async Task<IHttpActionResult> Archive(Guid id)
        {
            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            await _service.ArchiveAsync(id, userId);
            return Ok();
        }

        [HttpPost, Route("{id:guid}/$clone"), RequiresPermissionApi(Permissions.QuestionnaireWrite)]
        public async Task<IHttpActionResult> Clone(Guid id, [FromBody] CloneBody body)
        {
            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            var copy = await _service.CloneAsync(id, body?.Title, userId);
            var response = Request.CreateResponse(HttpStatusCode.Created, ToFhir(copy));
            response.Headers.ETag = ETagFor(copy);
            return ResponseMessage(response);
        }

        [HttpDelete, Route("{id:guid}"), RequiresPermissionApi(Permissions.QuestionnaireDelete)]
        public async Task<IHttpActionResult> Delete(Guid id)
        {
            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            var outcome = await _service.DeleteAsync(id, userId);
            // Un 204 acá sería mentira: el recurso sigue existiendo, archivado.
            if (outcome == DeleteOutcome.ArchivedBecauseInUse)
            {
                return Ok(OperationOutcomes.Informational(
                    "El cuestionario tiene respuestas cargadas: se archivó en vez de eliminarse."));
            }
            return StatusCode(HttpStatusCode.NoContent);
        }

        public class PublishBody { public string ChangeNote { get; set; } }
        public class CloneBody { public string Title { get; set; } }
    }
}
