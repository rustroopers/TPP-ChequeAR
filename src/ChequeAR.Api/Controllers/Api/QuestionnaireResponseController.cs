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
            return Ok(new { resourceType = "Bundle", type = "searchset", total = items.Count, entry = items });
        }

        [HttpGet, Route("{id:guid}"), Authorize]
        public async Task<IHttpActionResult> Get(Guid id)
        {
            var r = await _repo.GetAsync(id);
            if (r == null) return NotFound();
            return Ok(r);
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
            catch (InvalidOperationException ex) { return Content(HttpStatusCode.UnprocessableEntity, new { error = ex.Message }); }
        }

        [HttpPost, Route("{id:guid}/$finalize"), Authorize(Roles = Role.Administrador + "," + Role.Jefe + "," + Role.Medico)]
        public async Task<IHttpActionResult> Finalize(Guid id)
        {
            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            try
            {
                var r = await _service.FinalizeAsync(id, userId);
                return Ok(r);
            }
            catch (InvalidOperationException ex)
            {
                return Content(HttpStatusCode.UnprocessableEntity, new { error = ex.Message });
            }
        }
    }
}
