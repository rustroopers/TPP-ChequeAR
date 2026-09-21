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
            var pagina = await _users.ListAsync(q, role, specialty, active, _offset, _count);
            return Ok(new { total = pagina.Total, results = pagina.Items.Select(ToDto) });
        }

        [HttpGet, Route("{id}"), RequiresPermissionApi(Permissions.UserManage)]
        public async Task<IHttpActionResult> Get(string id)
        {
            var u = await _users.GetAsync(id);
            if (u == null) return NotFound();
            return Ok(ToDto(u));
        }

        [HttpPut, Route("{id}"), RequiresPermissionApi(Permissions.UserManage)]
        public async Task<IHttpActionResult> Upsert(string id, [FromBody] User user)
        {
            if (user == null) return BadRequest("body required");
            user.Id = id;
            // PasswordHash NUNCA se toca desde este endpoint: ni para leerlo (ver ToDto) ni
            // para escribirlo -- si el body lo trae (el modelo lo expone por ser la misma
            // entidad de dominio), se ignora explícitamente. UserRepository.UpsertAsync
            // también lo preserva por su cuenta; esto es cinturón y tirantes.
            user.PasswordHash = null;
            user.PasswordUpdatedAt = null;
            await _users.UpsertAsync(user);
            return StatusCode(HttpStatusCode.NoContent);
        }
    }
}
