using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using ChequeAR.Application.Dtos;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Services;

namespace ChequeAR.Api.Controllers.Api
{
    [RoutePrefix("auth")]
    public class AuthController : ApiController
    {
        private readonly ICredentialAuthenticator _auth;
        private readonly JwtIssuer _jwt;
        private readonly RefreshTokenService _refresh;
        private readonly UserService _users;

        public AuthController(ICredentialAuthenticator auth, JwtIssuer jwt, RefreshTokenService refresh, UserService users)
        {
            _auth = auth;
            _jwt = jwt;
            _refresh = refresh;
            _users = users;
        }

        public class LoginRequest
        {
            public string Username { get; set; }
            public string Password { get; set; }
        }

        [HttpPost, Route("login"), AllowAnonymous]
        public async Task<IHttpActionResult> Login([FromBody] LoginRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Username))
                return BadRequest("username required");

            var ip = ((HttpRequestMessage)Request).GetOwinContext()?.Request?.RemoteIpAddress;
            if (Auth.LoginThrottle.IsBlocked(ip))
            {
                Serilog.Log.Warning("Login (API) rechazado por exceso de intentos desde {Ip}", ip);
                return StatusCode((HttpStatusCode)429);
            }

            var user = await _auth.LoginAsync(req.Username, req.Password);
            if (user == null)
            {
                Auth.LoginThrottle.RegisterFailure(ip);
                Serilog.Log.Warning("Login (API) fallido para {User} desde {Ip}", await _users.LoggableUserNameAsync(req.Username), ip);
                await _users.LoginFailedAsync(req.Username);
                return StatusCode(HttpStatusCode.Unauthorized);
            }

            // El espejo se actualiza ANTES de emitir el token: si el sync falla, no se
            // emite un access token con permisos que la base no respalda. Devuelve el
            // UserInfo saneado (códigos de especialidad filtrados contra el catálogo).
            user = await _users.SyncFromHospitalAsync(user);
            if (user == null) return StatusCode(HttpStatusCode.Unauthorized);
            _users.LoginSucceeded(user.Id);

            var access = _jwt.IssueAccessToken(user);
            var ua = Request.Headers.UserAgent?.ToString();
            var refresh = await _refresh.IssueAsync(user.Id, ua, ip);

            return Ok(new LoginResult
            {
                AccessToken = access,
                RefreshToken = refresh,
                ExpiresIn = _jwt.AccessTtlSeconds,
                User = user
            });
        }

        public class RefreshRequest { public string RefreshToken { get; set; } }

        [HttpPost, Route("refresh"), AllowAnonymous]
        public async Task<IHttpActionResult> Refresh([FromBody] RefreshRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.RefreshToken)) return BadRequest();
            var t = await _refresh.ValidateAsync(req.RefreshToken);
            if (t == null) return StatusCode(HttpStatusCode.Unauthorized);

            // Recargar roles y especialidades desde el espejo local: antes el token
            // refrescado salía SIN ninguno de los dos, así que a los 60 minutos (el TTL
            // del access token) el usuario perdía acceso a todo lo que exigiera rol o
            // especialidad, aunque su sesión siguiera "vigente" para el refresh token.
            //
            // Esto convierte al refresh en el mecanismo de revocación: una especialidad
            // sacada (o el usuario desactivado) en el hospital se propaga en <= 60 min
            // sin esperar un re-login.
            var u = await _users.GetAsync(t.UserId);
            if (u == null || !u.IsActive)
            {
                await _refresh.RevokeAsync(req.RefreshToken);
                return StatusCode(HttpStatusCode.Unauthorized);
            }

            var user = new UserInfo
            {
                Id = u.Id,
                DisplayName = u.DisplayName ?? u.Id,
                Roles = (u.Roles ?? new List<ChequeAR.Domain.Entities.UserRole>()).Select(r => r.Role).ToList(),
                Specialties = (u.Specialties ?? new List<ChequeAR.Domain.Entities.UserSpecialty>()).Select(s => s.SpecialtyCode).ToList(),
                SecurityStamp = u.SecurityStamp
            };
            var access = _jwt.IssueAccessToken(user);
            return Ok(new LoginResult { AccessToken = access, ExpiresIn = _jwt.AccessTtlSeconds, User = user });
        }

        [HttpPost, Route("logout"), Authorize]
        public async Task<IHttpActionResult> Logout([FromBody] RefreshRequest req)
        {
            if (req != null && !string.IsNullOrEmpty(req.RefreshToken))
                await _refresh.RevokeAsync(req.RefreshToken);
            _users.LoggedOut(Auth.CurrentUser.Id(User as System.Security.Claims.ClaimsPrincipal));
            return Ok();
        }

        [HttpGet, Route("me"), Authorize]
        public IHttpActionResult Me()
        {
            var principal = User as System.Security.Claims.ClaimsPrincipal;
            return Ok(new
            {
                id = Auth.CurrentUser.Id(principal),
                roles = Auth.CurrentUser.Roles(principal),
                specialties = Auth.CurrentUser.Specialties(principal)
            });
        }
    }
}
