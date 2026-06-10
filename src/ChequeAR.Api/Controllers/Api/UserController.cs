using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Api.Controllers.Api
{
    /// Endpoints de gestión de usuarios (A4 listado, A5 detalle/edición).
    /// La existencia depende de si ChequeAR mantiene su propio espejo del padrón
    /// hospitalario (ver `dudas.md` y spec 005).
    [RoutePrefix("api/users")]
    public class UserController : ApiController
    {
        private readonly IUserRepository _repo;
        public UserController(IUserRepository repo) { _repo = repo; }

        [HttpGet, Route(""), Authorize(Roles = Role.Administrador)]
        public async Task<IHttpActionResult> List(
            [FromUri] string q = null, [FromUri] string role = null, [FromUri] string specialty = null,
            [FromUri] bool? active = null, [FromUri] int _count = 50, [FromUri] int _offset = 0)
        {
            var items = await _repo.ListAsync(q, role, specialty, active, _offset, _count);
            var total = await _repo.CountAsync(q, role, specialty, active);
            return Ok(new { total = total, results = items });
        }

        [HttpGet, Route("{id}"), Authorize(Roles = Role.Administrador)]
        public async Task<IHttpActionResult> Get(string id)
        {
            var u = await _repo.GetAsync(id);
            if (u == null) return NotFound();
            return Ok(u);
        }

        [HttpPut, Route("{id}"), Authorize(Roles = Role.Administrador)]
        public async Task<IHttpActionResult> Upsert(string id, [FromBody] User user)
        {
            if (user == null) return BadRequest("body required");
            user.Id = id;
            await _repo.UpsertAsync(user);
            return StatusCode(HttpStatusCode.NoContent);
        }
    }
}
