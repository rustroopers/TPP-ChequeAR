using System;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using ChequeAR.Api.Auth;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Api.Controllers.Api
{
    /// CRUD de consultas guardadas (E1 listado, E6 modal guardar).
    /// Spec: specs/004-analisis-exportacion/api.md §`/api/reports/saved`.
    [RoutePrefix("api/reports/saved")]
    public class SavedReportController : ApiController
    {
        private readonly ISavedReportRepository _repo;
        private readonly IQuestionnaireRepository _qrepo;
        public SavedReportController(ISavedReportRepository repo, IQuestionnaireRepository qrepo)
        {
            _repo = repo;
            _qrepo = qrepo;
        }

        [HttpGet, Route(""), Authorize]
        public async Task<IHttpActionResult> List()
        {
            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            var specialties = CurrentUser.Specialties(User as ClaimsPrincipal);
            var items = await _repo.ListForUserAsync(userId, specialties);
            return Ok(new { total = items.Count, results = items });
        }

        [HttpGet, Route("{id:guid}"), Authorize]
        public async Task<IHttpActionResult> Get(Guid id)
        {
            var r = await _repo.GetAsync(id);
            if (r == null) return NotFound();
            return Ok(r);
        }

        [HttpPost, Route(""), Authorize]
        public async Task<IHttpActionResult> Create([FromBody] SavedReport body)
        {
            if (body == null) return BadRequest("body required");
            body.Id = Guid.NewGuid();
            body.CreatedBy = CurrentUser.Id(User as ClaimsPrincipal);
            body.CreatedAt = DateTime.UtcNow;
            body.UpdatedAt = DateTime.UtcNow;
            await _repo.AddAsync(body);
            return Content(HttpStatusCode.Created, body);
        }

        [HttpPut, Route("{id:guid}"), Authorize]
        public async Task<IHttpActionResult> Update(Guid id, [FromBody] SavedReport body)
        {
            if (body == null) return BadRequest("body required");
            var existing = await _repo.GetAsync(id);
            if (existing == null) return NotFound();
            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            if (existing.CreatedBy != userId) return StatusCode(HttpStatusCode.Forbidden);

            existing.Name = body.Name ?? existing.Name;
            existing.Scope = body.Scope ?? existing.Scope;
            existing.SpecialtyCode = body.SpecialtyCode ?? existing.SpecialtyCode;
            existing.QueryJson = body.QueryJson ?? existing.QueryJson;
            existing.UpdatedAt = DateTime.UtcNow;
            await _repo.UpdateAsync(existing);
            return Ok(existing);
        }

        [HttpDelete, Route("{id:guid}"), Authorize]
        public async Task<IHttpActionResult> Delete(Guid id)
        {
            var existing = await _repo.GetAsync(id);
            if (existing == null) return NotFound();
            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            if (existing.CreatedBy != userId) return StatusCode(HttpStatusCode.Forbidden);
            await _repo.DeleteAsync(id);
            return StatusCode(HttpStatusCode.NoContent);
        }

        // GET /api/reports/preview-columns?questionnaireId={guid}
        // Devuelve linkIds + types disponibles para el query builder (E2).
        [HttpGet, Route("~/api/reports/preview-columns"), Authorize]
        public async Task<IHttpActionResult> PreviewColumns([FromUri] Guid questionnaireId)
        {
            var q = await _qrepo.GetAsync(questionnaireId);
            if (q == null) return NotFound();
            var cols = new System.Collections.Generic.List<object>();
            if (q.Questions != null)
            {
                foreach (var qq in q.Questions)
                {
                    cols.Add(new { linkId = qq.LinkId, text = qq.Text, type = qq.Type.ToString().ToLowerInvariant() });
                }
            }
            return Ok(new { questionnaireId = questionnaireId, columns = cols });
        }
    }
}
