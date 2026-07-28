using System.Collections.Generic;
using System.Linq;
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
            var all = await _repo.SearchAsync(agentWho, null, null, null, 5000);
            IEnumerable<Domain.Entities.AuditEvent> filtered = all;
            if (!string.IsNullOrEmpty(entityWhat)) filtered = filtered.Where(x => x.EntityRef == entityWhat);
            var list = filtered.ToList();
            var entry = list.Skip(_offset).Take(_count).ToList();
            return Ok(new { resourceType = "Bundle", type = "searchset", total = list.Count, entry });
        }

        // A7 — detalle de evento. Búsqueda por id directo (no FHIR-search).
        [HttpGet, Route("{id:guid}"), Authorize(Roles = Role.Administrador + "," + Role.Jefe)]
        public async Task<IHttpActionResult> Get(System.Guid id)
        {
            // Reusa SearchAsync con paginación amplia + filtro client-side.
            // Optimización futura: agregar IAuditRepository.GetAsync(id).
            var all = await _repo.SearchAsync(null, null, null, null, 5000);
            foreach (var e in all)
            {
                if (e.Id == id) return Ok(e);
            }
            return NotFound();
        }
    }
}
