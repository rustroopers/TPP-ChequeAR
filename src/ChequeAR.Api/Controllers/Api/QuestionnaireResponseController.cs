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
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Api.Controllers.Api
{
    [RoutePrefix("fhir/r5/QuestionnaireResponse")]
    public class QuestionnaireResponseController : ApiController
    {
        private readonly IQuestionnaireResponseRepository _repo;
        private readonly QuestionnaireResponseService _service;

        public QuestionnaireResponseController(IQuestionnaireResponseRepository repo, QuestionnaireResponseService service)
        {
            _repo = repo;
            _service = service;
        }

        [HttpGet, Route(""), Authorize]
        public async Task<IHttpActionResult> Search([FromUri] string subject = null, [FromUri] Guid? questionnaire = null, [FromUri] string status = null, [FromUri] int _count = 50, [FromUri] int _offset = 0)
        {
            ResponseStatus? st = null;
            if (Enum.TryParse(status, true, out ResponseStatus s)) st = s;
            var items = await _repo.SearchAsync(subject, questionnaire, st, _offset, _count);
            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            foreach (var r in items) HideLockTokenFromOthers(r, userId);
            return Ok(new { resourceType = "Bundle", type = "searchset", total = items.Count, entry = items });
        }

        [HttpGet, Route("{id:guid}"), Authorize]
        public async Task<IHttpActionResult> Get(Guid id)
        {
            var r = await _repo.GetAsync(id);
            if (r == null) return NotFound();

            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            HideLockTokenFromOthers(r, userId);

            var response = Request.CreateResponse(HttpStatusCode.OK, r);
            response.Headers.ETag = ETagFor(r);
            return ResponseMessage(response);
        }

        // El token es la credencial de la sesión de edición: mostrárselo a otro usuario le
        // permitiría suplantar al que tiene el lock. Sólo su dueño lo ve.
        private static void HideLockTokenFromOthers(QuestionnaireResponse r, string userId)
        {
            if (r.LockedByUserId != userId) r.LockToken = null;
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

        [HttpPost, Route(""), Authorize(Roles = Role.Administrador + "," + Role.Jefe + "," + Role.Medico)]
        public async Task<IHttpActionResult> Create([FromBody] QuestionnaireResponse r)
        {
            if (r == null) return BadRequest("body required");
            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            try
            {
                var created = await _service.CreateAsync(r, userId);
                return Content(HttpStatusCode.Created, created);
            }
            catch (ArgumentException ex) { return BadRequest(ex.Message); }
            catch (InvalidOperationException ex) { return Content((HttpStatusCode)422, new { error = ex.Message }); }
        }

        // Toma la sesión de edición exclusiva. Sin esto, dos médicos podrían completar el
        // mismo cuestionario a la vez (ej. dos llamadas de seguimiento superpuestas) y
        // pisarse las respuestas.
        [HttpPost, Route("{id:guid}/$lock"), Authorize(Roles = Role.Administrador + "," + Role.Jefe + "," + Role.Medico)]
        public async Task<IHttpActionResult> Lock(Guid id)
        {
            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            try
            {
                var r = await _service.AcquireLockAsync(id, userId);
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
            catch (LockConflictException ex)
            {
                return Content(HttpStatusCode.Conflict, new { error = ex.Message, lockedBy = ex.LockedByUserId, expiresAt = ex.LockExpiresAt });
            }
            catch (InvalidOperationException ex)
            {
                return Content((HttpStatusCode)422, new { error = ex.Message });
            }
        }

        // Heartbeat: extiende el vencimiento sin escribir respuestas. Lo dispara el aviso
        // de "1 minuto para que se cierre" cuando el médico confirma que sigue en la llamada.
        [HttpPost, Route("{id:guid}/$renew-lock"), Authorize(Roles = Role.Administrador + "," + Role.Jefe + "," + Role.Medico)]
        public async Task<IHttpActionResult> RenewLock(Guid id)
        {
            var token = ParseLockToken(Request);
            if (!token.HasValue)
                return Content((HttpStatusCode)428, new { error = "Falta el header X-Lock-Token." });

            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            try
            {
                var r = await _service.RenewLockAsync(id, userId, token.Value);
                return Ok(new { expiresAt = r.LockExpiresAt, concurrencyVersion = r.ConcurrencyVersion });
            }
            catch (LockConflictException ex)
            {
                return Content(HttpStatusCode.Conflict, new { error = ex.Message, lockedBy = ex.LockedByUserId, expiresAt = ex.LockExpiresAt });
            }
            catch (InvalidOperationException ex)
            {
                return Content((HttpStatusCode)422, new { error = ex.Message });
            }
        }

        // Cierra la sesión de edición (fin de la llamada, o navegación fuera de la página).
        // Idempotente: sin token, o con un token que ya no es el vigente, no hay nada que
        // liberar y no es un error -- evita que un cierre tardío rompa la sesión de otro.
        [HttpPost, Route("{id:guid}/$unlock"), Authorize(Roles = Role.Administrador + "," + Role.Jefe + "," + Role.Medico)]
        public async Task<IHttpActionResult> Unlock(Guid id)
        {
            var token = ParseLockToken(Request);
            if (!token.HasValue) return Ok();

            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            await _service.ReleaseLockAsync(id, userId, token.Value);
            return Ok();
        }

        // Guardado parcial (autosave de una llamada): exige el lock de sesión (X-Lock-Token)
        // además del If-Match de siempre, porque acá se protegen dos cosas distintas -- que
        // nadie más esté en medio de la misma sesión de edición, y que nadie haya guardado
        // una versión más nueva desde la última lectura de este cliente.
        [HttpPut, Route("{id:guid}"), Authorize(Roles = Role.Administrador + "," + Role.Jefe + "," + Role.Medico)]
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
            try
            {
                var r = await _service.SaveProgressAsync(id, userId, lockToken.Value, expectedVersion, body?.Items);
                var response = Request.CreateResponse(HttpStatusCode.OK, r);
                response.Headers.ETag = ETagFor(r);
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
            catch (LockConflictException ex)
            {
                return Content(HttpStatusCode.Conflict, new { error = ex.Message, lockedBy = ex.LockedByUserId, expiresAt = ex.LockExpiresAt });
            }
            catch (InvalidOperationException ex)
            {
                return Content((HttpStatusCode)422, new { error = ex.Message });
            }
        }

        [HttpPost, Route("{id:guid}/$finalize"), Authorize(Roles = Role.Administrador + "," + Role.Jefe + "," + Role.Medico)]
        public async Task<IHttpActionResult> Finalize(Guid id)
        {
            var lockToken = ParseLockToken(Request);
            if (!lockToken.HasValue)
                return Content((HttpStatusCode)428, new { error = "Falta el header X-Lock-Token." });

            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            try
            {
                var r = await _service.FinalizeAsync(id, userId, lockToken.Value);
                return Ok(r);
            }
            catch (LockConflictException ex)
            {
                return Content(HttpStatusCode.Conflict, new { error = ex.Message, lockedBy = ex.LockedByUserId, expiresAt = ex.LockExpiresAt });
            }
            catch (InvalidOperationException ex)
            {
                return Content((HttpStatusCode)422, new { error = ex.Message });
            }
        }

        public class SaveProgressBody { public List<QuestionnaireResponseItem> Items { get; set; } }
    }
}
