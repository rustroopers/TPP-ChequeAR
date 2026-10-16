using System;
using System.Collections.Generic;
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
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Api.Controllers.Api
{
    [RoutePrefix("fhir/r5/QuestionnaireResponse")]
    public class QuestionnaireResponseController : ApiController
    {
        private readonly QuestionnaireResponseService _service;
        private readonly AccessAuditService _accessAudit;

        public QuestionnaireResponseController(QuestionnaireResponseService service, AccessAuditService accessAudit)
        {
            _service = service;
            _accessAudit = accessAudit;
        }

        [HttpGet, Route(""), Authorize]
        public async Task<IHttpActionResult> Search([FromUri] string subject = null, [FromUri] Guid? questionnaire = null, [FromUri] string status = null, [FromUri] int _count = 50, [FromUri] int _offset = 0, [FromUri] string _summary = null)
        {
            ResponseStatus? st = null;
            if (Enum.TryParse(status, true, out ResponseStatus s)) st = s;
            var items = await _service.SearchAsync(subject, questionnaire, st,
                FhirBundles.ClampOffset(_offset), FhirBundles.ClampCount(_count));
            var total = await _service.CountAsync(subject, questionnaire, st);

            // Con _summary=true no viajan respuestas y no se audita; sin él, cada ficha cuenta como lectura.
            var resumen = string.Equals(_summary, "true", StringComparison.OrdinalIgnoreCase);
            if (!resumen)
                foreach (var r in items) await _accessAudit.ResponseRead(r);

            var recursos = items.Select(r => (Hl7.Fhir.Model.Resource)(resumen ? Summary(ToFhir(r)) : ToFhir(r)));
            return Ok(FhirBundles.SearchSet(recursos, total, FhirBaseUrl()));
        }

        private static Hl7.Fhir.Model.QuestionnaireResponse Summary(Hl7.Fhir.Model.QuestionnaireResponse resource)
        {
            resource.Item.Clear();
            resource.Meta.Tag.Add(new Hl7.Fhir.Model.Coding("http://terminology.hl7.org/CodeSystem/v3-ObservationValue", "SUBSETTED"));
            return resource;
        }

        [HttpGet, Route("{id:guid}"), Authorize]
        public async Task<IHttpActionResult> Get(Guid id)
        {
            QuestionnaireResponse r;
            try
            {
                r = await _service.GetAsync(id);
            }
            catch (SpecialtyForbiddenException)
            {
                return Content(HttpStatusCode.NotFound, OperationOutcomes.NotFound());
            }
            if (r == null) return Content(HttpStatusCode.NotFound, OperationOutcomes.NotFound());
            await _accessAudit.ResponseRead(r);

            var response = Request.CreateResponse(HttpStatusCode.OK, ToFhir(r));
            response.Headers.ETag = ETagFor(r);
            return ResponseMessage(response);
        }

        /// <summary>
        /// Devuelve el recurso ALMACENADO, no uno regenerado: FhirJson es la representación
        /// canónica de la ficha —es lo que validó $finalize y lo que conserva los textos de la
        /// versión anclada y el paciente embebido—. Regenerarlo acá, sin la versión a mano,
        /// perdería los textos que el médico leyó.
        ///
        /// Sólo se reconstruye si el guardado está vacío o ilegible, para que una fila corrupta
        /// degrade en vez de tumbar la búsqueda entera.
        ///
        /// La versión de concurrencia SIEMPRE sale fresca de la entidad viva (no de lo
        /// guardado): el FhirJson persistido se re-arma en cada escritura ANTES de incrementar
        /// ConcurrencyVersion (ver QuestionnaireResponseService), así que el valor embebido ahí
        /// quedaría atrasado. meta.versionId es, para el cliente, el reemplazo FHIR-idiomático
        /// del viejo campo ConcurrencyVersion: mismo mecanismo de "Managing Resource
        /// Contention" que ya se usa con ETag/If-Match, expuesto también en el cuerpo.
        /// </summary>
        private static Hl7.Fhir.Model.QuestionnaireResponse ToFhir(QuestionnaireResponse r)
        {
            var almacenado = FhirResourceReader.ReadResponse(r.FhirJson);
            var resource = almacenado ?? FhirQuestionnaireResponseBuilder.ToResource(r, null);
            resource.Meta = resource.Meta ?? new Hl7.Fhir.Model.Meta();
            resource.Meta.VersionId = r.ConcurrencyVersion.ToString();
            return resource;
        }

        private static EntityTagHeaderValue ETagFor(QuestionnaireResponse r)
        {
            return new EntityTagHeaderValue("\"" + r.ConcurrencyVersion + "\"", true);
        }

        // Acepta W/"3" y "3". Devuelve null si no hay un número adentro.
        private static int? ParseIfMatch(HttpRequestMessage request)
        {
            var tag = request.Headers.IfMatch?.FirstOrDefault();
            if (tag == null) return null;
            var raw = (tag.Tag ?? string.Empty).Trim('"');
            return int.TryParse(raw, out var version) ? version : (int?)null;
        }

        private static Guid? ParseLockToken(HttpRequestMessage request)
        {
            if (!request.Headers.TryGetValues("X-Lock-Token", out var values)) return null;
            return Guid.TryParse(values.FirstOrDefault(), out var token) ? token : (Guid?)null;
        }

        private string FhirBaseUrl()
            => Request.RequestUri.GetLeftPart(UriPartial.Authority) + "/fhir/r5";

        [HttpPost, Route(""), RequiresPermissionApi(Permissions.EncounterWrite)]
        public async Task<IHttpActionResult> Create([FromBody] Hl7.Fhir.Model.QuestionnaireResponse body)
        {
            var r = FhirQuestionnaireResponseParser.ToDomain(body);
            if (r == null) return Content(HttpStatusCode.BadRequest, OperationOutcomes.Invalid());

            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            var created = await _service.CreateAsync(r, userId);
            return Content(HttpStatusCode.Created, ToFhir(created));
        }

        // Toma la sesión de edición exclusiva. Sin esto, dos médicos podrían completar el
        // mismo cuestionario a la vez (ej. dos llamadas de seguimiento superpuestas) y
        // pisarse las respuestas.
        [HttpPost, Route("{id:guid}/$lock"), RequiresPermissionApi(Permissions.EncounterWrite)]
        public async Task<IHttpActionResult> Lock(Guid id)
        {
            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            var r = await _service.AcquireLockAsync(id, userId);
            // (el guard corre adentro del service, ANTES de tomar el lock: ver AcquireLockAsync)
            // $lock también bumpea ConcurrencyVersion (es una escritura más, protegida
            // por el mismo token que cualquier otra). El cliente tiene que enterarse acá
            // y no sólo por el ETag -- si no, el primer guardado después de tomar el
            // lock manda el If-Match viejo y choca con un 409 que no es un conflicto real.
            var response = Request.CreateResponse(HttpStatusCode.OK, new
            {
                lockToken = r.LockToken,
                expiresAt = r.LockExpiresAt,
                concurrencyVersion = r.ConcurrencyVersion
            });
            response.Headers.ETag = ETagFor(r);
            return ResponseMessage(response);
        }

        // Heartbeat: extiende el vencimiento sin escribir respuestas. Lo dispara el aviso
        // de "1 minuto para que se cierre" cuando el médico confirma que sigue en la llamada.
        [HttpPost, Route("{id:guid}/$renew-lock"), RequiresPermissionApi(Permissions.EncounterWrite)]
        public async Task<IHttpActionResult> RenewLock(Guid id)
        {
            var token = ParseLockToken(Request);
            if (!token.HasValue)
                return Content((HttpStatusCode)428, new { error = "Falta el header X-Lock-Token." });

            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            var r = await _service.RenewLockAsync(id, userId, token.Value);
            return Ok(new { expiresAt = r.LockExpiresAt, concurrencyVersion = r.ConcurrencyVersion });
        }

        // Cierra la sesión de edición (fin de la llamada, o navegación fuera de la página).
        // Idempotente: sin token, o con un token que ya no es el vigente, no hay nada que
        // liberar y no es un error -- evita que un cierre tardío rompa la sesión de otro.
        // El body (opcional) lleva el baseline de la sesión: las respuestas como estaban al
        // abrirla. Sólo lo manda el "Guardar avance" deliberado; con él, ReleaseLock audita un
        // único evento con el diff de lo completado. El cierre pasivo de pestaña no lo manda.
        [HttpPost, Route("{id:guid}/$unlock"), RequiresPermissionApi(Permissions.EncounterWrite)]
        public async Task<IHttpActionResult> Unlock(Guid id, [FromBody] UnlockBody body)
        {
            var token = ParseLockToken(Request);
            if (!token.HasValue) return Ok();

            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            await _service.ReleaseLockAsync(id, userId, token.Value, body?.Items, body?.Pending);
            return Ok();
        }

        // Guardado parcial (autosave de una llamada): exige el lock de sesión (X-Lock-Token)
        // además del If-Match de siempre, porque acá se protegen dos cosas distintas -- que
        // nadie más esté en medio de la misma sesión de edición, y que nadie haya guardado
        // una versión más nueva desde la última lectura de este cliente.
        //
        // El body sigue siendo la forma interna (LinkId/ValueXxx) y no un recurso FHIR: es el
        // formato de guardado incremental, no la representación canónica de la ficha -- esa
        // sigue siendo GET/Search/$finalize, que sí devuelven el recurso FHIR completo.
        [HttpPut, Route("{id:guid}"), RequiresPermissionApi(Permissions.EncounterWrite)]
        public async Task<IHttpActionResult> SaveProgress(Guid id, [FromBody] SaveProgressBody body)
        {
            var expectedVersion = ParseIfMatch(Request);
            if (!expectedVersion.HasValue)
            {
                return Content((HttpStatusCode)428, new
                {
                    error = "Falta el header If-Match con la versión de la ficha (ej: If-Match: W/\"3\")."
                });
            }

            var lockToken = ParseLockToken(Request);
            if (!lockToken.HasValue)
            {
                return Content((HttpStatusCode)428, new
                {
                    error = "Falta el header X-Lock-Token: hay que tomar el lock ($lock) antes de guardar."
                });
            }

            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            var r = await _service.SaveProgressAsync(id, userId, lockToken.Value, expectedVersion, body?.Items);
            var response = Request.CreateResponse(HttpStatusCode.OK, ToFhir(r));
            response.Headers.ETag = ETagFor(r);
            // El vencimiento del lock no tiene lugar nativo en FHIR (no es parte del
            // recurso canónico): viaja en un header propio, igual que X-Lock-Token en el
            // request. El cliente lo usa para reprogramar el aviso de "1 minuto".
            if (r.LockExpiresAt.HasValue)
                response.Headers.Add("X-Lock-Expires-At", r.LockExpiresAt.Value.ToString("o"));
            return ResponseMessage(response);
        }

        // El body (opcional) lleva el baseline de la sesión: las respuestas como estaban al
        // abrirla. Con él, la finalización audita también el diff de lo que cambió en la última
        // tanda (A→B), además de la foto completa. Fichas viejas / clientes que no lo mandan
        // siguen funcionando: sólo no habrá diff de la sesión final.
        [HttpPost, Route("{id:guid}/$finalize"), RequiresPermissionApi(Permissions.EncounterWrite)]
        public async Task<IHttpActionResult> Finalize(Guid id, [FromBody] SaveProgressBody body)
        {
            var lockToken = ParseLockToken(Request);
            if (!lockToken.HasValue)
                return Content((HttpStatusCode)428, new { error = "Falta el header X-Lock-Token." });

            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            var r = await _service.FinalizeAsync(id, userId, lockToken.Value, body?.Items);
            var response = Request.CreateResponse(HttpStatusCode.OK, ToFhir(r));
            response.Headers.ETag = ETagFor(r);
            return ResponseMessage(response);
        }

        // Corrección de una ficha finalizada: $amend abre la sesión (como $lock) y
        // $amend-commit guarda todo junto, con el motivo. No hay autoguardado.
        [HttpPost, Route("{id:guid}/$amend"), RequiresPermissionApi(Permissions.EncounterWrite)]
        public async Task<IHttpActionResult> Amend(Guid id)
        {
            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            var r = await _service.BeginAmendAsync(id, userId);
            var response = Request.CreateResponse(HttpStatusCode.OK, new
            {
                lockToken = r.LockToken,
                expiresAt = r.LockExpiresAt,
                concurrencyVersion = r.ConcurrencyVersion
            });
            response.Headers.ETag = ETagFor(r);
            return ResponseMessage(response);
        }

        [HttpPost, Route("{id:guid}/$amend-commit"), RequiresPermissionApi(Permissions.EncounterWrite)]
        public async Task<IHttpActionResult> AmendCommit(Guid id, [FromBody] AmendBody body)
        {
            var lockToken = ParseLockToken(Request);
            if (!lockToken.HasValue)
                return Content((HttpStatusCode)428, new { error = "Falta el header X-Lock-Token." });

            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            var r = await _service.CommitAmendAsync(id, userId, lockToken.Value, ParseIfMatch(Request), body?.Items, body?.Reason);
            var response = Request.CreateResponse(HttpStatusCode.OK, ToFhir(r));
            response.Headers.ETag = ETagFor(r);
            return ResponseMessage(response);
        }

        // Descarta un borrador InProgress (el médico eligió "Empezar de nuevo" en vez de
        // retomarlo). No exige X-Lock-Token: a diferencia de $finalize/$save, esto no cierra
        // una sesión de edición propia sino que se dispara ANTES de abrir una, desde el
        // selector de paciente. El service igual rechaza si otro médico la tiene bloqueada.
        [HttpPost, Route("{id:guid}/$abandon"), RequiresPermissionApi(Permissions.EncounterWrite)]
        public async Task<IHttpActionResult> Abandon(Guid id)
        {
            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            var r = await _service.AbandonAsync(id, userId);
            var response = Request.CreateResponse(HttpStatusCode.OK, ToFhir(r));
            response.Headers.ETag = ETagFor(r);
            return ResponseMessage(response);
        }

        // Vista previa de "pasar a la versión vigente": qué se conserva / se agrega / se
        // descartaría. Sólo lectura, no toca la ficha ni exige el lock.
        [HttpGet, Route("{id:guid}/$retarget-preview"), RequiresPermissionApi(Permissions.EncounterWrite)]
        public async Task<IHttpActionResult> RetargetPreview(Guid id)
        {
            try
            {
                return Ok(await _service.BuildRetargetPreviewAsync(id));
            }
            catch (SpecialtyForbiddenException)
            {
                return Content(HttpStatusCode.NotFound, OperationOutcomes.NotFound());
            }
        }

        // Re-ancla la ficha a la versión vigente (descarta lo que la versión nueva eliminó).
        // Exige el lock, igual que finalizar: es una escritura sobre una sesión de edición.
        [HttpPost, Route("{id:guid}/$retarget-version"), RequiresPermissionApi(Permissions.EncounterWrite)]
        public async Task<IHttpActionResult> RetargetVersion(Guid id)
        {
            var lockToken = ParseLockToken(Request);
            if (!lockToken.HasValue)
                return Content((HttpStatusCode)428, new { error = "Falta el header X-Lock-Token." });

            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            var r = await _service.RetargetToCurrentVersionAsync(id, userId, lockToken.Value);
            var response = Request.CreateResponse(HttpStatusCode.OK, ToFhir(r));
            response.Headers.ETag = ETagFor(r);
            return ResponseMessage(response);
        }

        public class SaveProgressBody { public List<QuestionnaireResponseItem> Items { get; set; } }

        public class UnlockBody
        {
            public List<QuestionnaireResponseItem> Items { get; set; }
            /// <summary>Respuestas que el autosave no llegó a mandar antes de salir de la página.</summary>
            public List<QuestionnaireResponseItem> Pending { get; set; }
        }

        public class AmendBody
        {
            public List<QuestionnaireResponseItem> Items { get; set; }
            public string Reason { get; set; }
        }
    }
}
