using System.Configuration;
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
        private readonly IHospitalAuthClient _hospitalAuth;
        private readonly JwtIssuer _jwt;
        private readonly RefreshTokenService _refresh;

        public AuthController(IHospitalAuthClient hospitalAuth, JwtIssuer jwt, RefreshTokenService refresh)
        {
            _hospitalAuth = hospitalAuth;
            _jwt = jwt;
            _refresh = refresh;
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

            var user = await _hospitalAuth.LoginAsync(req.Username, req.Password);
            if (user == null) return StatusCode(HttpStatusCode.Unauthorized);

            var access = _jwt.IssueAccessToken(user);
            var ua = Request.Headers.UserAgent?.ToString();
            var ip = ((HttpRequestMessage)Request).GetOwinContext()?.Request?.RemoteIpAddress;
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

            // Producción: recargar UserInfo desde la cache local (UserRepository).
            var user = new UserInfo { Id = t.UserId, DisplayName = t.UserId };
            var access = _jwt.IssueAccessToken(user);
            return Ok(new LoginResult { AccessToken = access, ExpiresIn = _jwt.AccessTtlSeconds, User = user });
        }

        [HttpPost, Route("logout"), Authorize]
        public async Task<IHttpActionResult> Logout([FromBody] RefreshRequest req)
        {
            if (req != null && !string.IsNullOrEmpty(req.RefreshToken))
                await _refresh.RevokeAsync(req.RefreshToken);
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
