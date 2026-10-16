using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using ChequeAR.Api.Auth;
using ChequeAR.Application.Services;
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
        private readonly UserService _users;
        public UserController(UserService users) { _users = users; }

        /// <summary>
        /// Nunca la entidad User completa: desde que tiene PasswordHash, serializarla tal
        /// cual expondría el hash (no la contraseña, pero sí material suficiente para un
        /// ataque de fuerza bruta offline) a cualquier admin que mire la respuesta del API.
        /// </summary>
        private static object ToDto(User u) => new
        {
            id = u.Id,
            displayName = u.DisplayName,
            email = u.Email,
            isActive = u.IsActive,
            lastLoginAt = u.LastLoginAt,
            lastSyncAt = u.LastSyncAt,
            roles = (u.Roles ?? new List<UserRole>()).Select(r => r.Role).ToList(),
            specialties = (u.Specialties ?? new List<UserSpecialty>()).Select(s => s.SpecialtyCode).ToList()
        };

        [HttpGet, Route(""), RequiresPermissionApi(Permissions.UserManage)]
        public async Task<IHttpActionResult> List(
            [FromUri] string q = null, [FromUri] string role = null, [FromUri] string specialty = null,
            [FromUri] bool? active = null, [FromUri] int _count = 50, [FromUri] int _offset = 0)
        {
            var take = System.Math.Min(System.Math.Max(_count, 1), 100);
            var pagina = await _users.ListAsync(q, role, specialty, active, System.Math.Max(_offset, 0), take);
            return Ok(new { total = pagina.Total, results = pagina.Items.Select(ToDto) });
        }

        [HttpGet, Route("{id}"), RequiresPermissionApi(Permissions.UserManage)]
        public async Task<IHttpActionResult> Get(string id)
        {
            var u = await _users.GetAsync(id);
            if (u == null) return NotFound();
            return Ok(ToDto(u));
        }

        /// <summary>Campos editables; no se enlaza a User para evitar asignación masiva.</summary>
        public class UserUpsertRequest
        {
            public string DisplayName { get; set; }
            public string Email { get; set; }
            public bool IsActive { get; set; }
        }

        [HttpPut, Route("{id}"), RequiresPermissionApi(Permissions.UserManage)]
        public async Task<IHttpActionResult> Upsert(string id, [FromBody] UserUpsertRequest body)
        {
            if (body == null) return BadRequest("body required");
            await _users.UpdateBasicsAsync(id, body.DisplayName, body.Email, body.IsActive,
                CurrentUser.Id(User as System.Security.Claims.ClaimsPrincipal));
            SessionRevalidation.Invalidate(id);
            return StatusCode(HttpStatusCode.NoContent);
        }
    }
}
