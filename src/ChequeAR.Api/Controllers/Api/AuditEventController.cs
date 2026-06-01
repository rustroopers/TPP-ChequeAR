using System.Threading.Tasks;
using System.Web.Http;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Api.Controllers.Api
{
    [RoutePrefix("fhir/r5/AuditEvent")]
    public class AuditEventController : ApiController
    {
        private readonly IAuditRepository _repo;
        public AuditEventController(IAuditRepository repo) { _repo = repo; }

        [HttpGet, Route(""), Authorize(Roles = Role.Administrador + "," + Role.Jefe)]
        public async Task<IHttpActionResult> Search([FromUri(Name = "agent.who")] string agentWho = null, [FromUri(Name = "entity.what")] string entityWhat = null, [FromUri] int _count = 50, [FromUri] int _offset = 0)
        {
            var items = await _repo.SearchAsync(agentWho, entityWhat, _offset, _count);
            return Ok(new { resourceType = "Bundle", type = "searchset", total = items.Count, entry = items });
        }
    }
}
