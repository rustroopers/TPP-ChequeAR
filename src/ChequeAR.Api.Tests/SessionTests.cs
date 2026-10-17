using System;
using System.Linq;
using System.Security.Claims;
using System.Web.Security;
using ChequeAR.Api.Auth;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Enums;
using Newtonsoft.Json;
using Xunit;

namespace ChequeAR.Api.Tests
{
    public class SessionTests
    {
        private static FormsAuthenticationTicket Ticket(object payload) =>
            new FormsAuthenticationTicket(3, "jefe.cardio", DateTime.Now, DateTime.Now.AddHours(1), false,
                JsonConvert.SerializeObject(payload));

        // El rol guardado es uno solo; los claims lo expanden por jerarquía, igual que el JWT.
        [Fact]
        public void ToPrincipal_ExpandeElRolYConservaEspecialidadesYSello()
        {
            var principal = SessionCookie.ToPrincipal(Ticket(new
            {
                DisplayName = "Jefe Cardio",
                Roles = new[] { Role.Jefe },
                Specialties = new[] { "cardio" },
                Stamp = "s1"
            }));

            Assert.True(principal.IsInRole(Role.Jefe));
            Assert.True(principal.IsInRole(Role.Medico));
            Assert.False(principal.IsInRole(Role.Administrador));
            Assert.Equal(new[] { "cardio" }, principal.FindAll("specialty").Select(c => c.Value));
            Assert.Equal("s1", principal.FindFirst(JwtIssuer.SecurityStampClaim).Value);
            Assert.Equal("jefe.cardio", principal.FindFirst(ClaimTypes.NameIdentifier).Value);
        }

        // Un ticket heredado con un rol que ya no existe no puede quedar con más privilegio.
        [Fact]
        public void ToPrincipal_RolDesconocido_QuedaComoMedico()
        {
            var principal = SessionCookie.ToPrincipal(Ticket(new { Roles = new[] { "Observador" } }));

            Assert.True(principal.IsInRole(Role.Medico));
            Assert.False(principal.IsInRole(Role.Jefe));
        }

        [Fact]
        public void ToUserInfo_IdaYVuelta()
        {
            var principal = SessionCookie.ToPrincipal(Ticket(new
            {
                DisplayName = "Admin",
                Roles = new[] { Role.Administrador },
                Specialties = new[] { "cardio", "nefro" },
                Stamp = "s2"
            }));

            var user = SessionCookie.ToUserInfo(principal);

            Assert.Equal("jefe.cardio", user.Id);
            Assert.Equal("Admin", user.DisplayName);
            Assert.Contains(Role.Administrador, user.Roles);
            Assert.Equal(new[] { "cardio", "nefro" }, user.Specialties);
            Assert.Equal("s2", user.SecurityStamp);
        }

        [Fact]
        public void ToUserInfo_SinAutenticar_Null()
        {
            Assert.Null(SessionCookie.ToUserInfo(new ClaimsPrincipal(new ClaimsIdentity())));
        }

        [Fact]
        public void LoginThrottle_BloqueaLaIpDespuesDelMaximo()
        {
            var ip = "test-" + Guid.NewGuid();
            for (var i = 0; i < LoginThrottle.MaxAttempts - 1; i++) LoginThrottle.RegisterFailure(ip);
            Assert.False(LoginThrottle.IsBlocked(ip));

            LoginThrottle.RegisterFailure(ip);

            Assert.True(LoginThrottle.IsBlocked(ip));
            Assert.False(LoginThrottle.IsBlocked(ip + "-otra"));
        }
    }
}
