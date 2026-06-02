using System;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using ChequeAR.Api.Auth;
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
            return Ok(q);
        }

        [HttpPost, Route(""), Authorize(Roles = Role.Administrador + "," + Role.Jefe)]
        public async Task<IHttpActionResult> Create([FromBody] Questionnaire q)
        {
            if (q == null) return BadRequest("body required");
            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            var created = await _service.CreateAsync(q, userId);
            return Content(HttpStatusCode.Created, created);
        }

        [HttpPut, Route("{id:guid}"), Authorize(Roles = Role.Administrador + "," + Role.Jefe)]
        public async Task<IHttpActionResult> Update(Guid id, [FromBody] Questionnaire patch)
        {
            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            var updated = await _service.UpdateAsync(id, patch, userId);
            return Ok(updated);
        }

        [HttpPost, Route("{id:guid}/$publish"), Authorize(Roles = Role.Administrador + "," + Role.Jefe)]
        public async Task<IHttpActionResult> Publish(Guid id, [FromBody] PublishBody body)
        {
            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            try
            {
                var q = await _service.PublishAsync(id, body?.FhirJson, userId);
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

        [HttpDelete, Route("{id:guid}"), Authorize(Roles = Role.Administrador)]
        public async Task<IHttpActionResult> Delete(Guid id)
        {
            await _repo.SoftDeleteAsync(id);
            return StatusCode(HttpStatusCode.NoContent);
        }

        public class PublishBody { public string FhirJson { get; set; } }
    }
}
