using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using ChequeAR.Application.Dtos;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Enums;
using Xunit;

namespace ChequeAR.Tests.Application
{
    /// <summary>
    /// El claim de rol es acumulativo (ver RoleHierarchy): un Jefe recibe claims Jefe+Medico,
    /// para que [Authorize(Roles = Role.Medico)] siga significando "Medico o superior". El
    /// rol canónico (sin expandir) viaja aparte en "role_principal", para la UI.
    /// </summary>
    public class JwtIssuerTests
    {
        private static JwtIssuer Sut() => new JwtIssuer("https://test", "test-aud", "un-secreto-de-prueba-bien-largo-para-hmac-sha256", 60);

        private static List<Claim> DecodeClaims(string token)
            => new JwtSecurityTokenHandler().ReadJwtToken(token).Claims.ToList();

        [Fact]
        public void IssueAccessToken_Administrador_ExpandeALosTresRoles()
        {
            var token = Sut().IssueAccessToken(new UserInfo { Id = "admin.demo", DisplayName = "Admin", Roles = new List<string> { Role.Administrador } });
            var roles = DecodeClaims(token).Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList();

            Assert.Contains(Role.Administrador, roles);
            Assert.Contains(Role.Jefe, roles);
            Assert.Contains(Role.Medico, roles);
        }

        [Fact]
        public void IssueAccessToken_Jefe_ExpandeAJefeYMedico_NoAdministrador()
        {
            var token = Sut().IssueAccessToken(new UserInfo { Id = "jefe.cardio", DisplayName = "Jefe", Roles = new List<string> { Role.Jefe } });
            var roles = DecodeClaims(token).Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList();

            Assert.Contains(Role.Jefe, roles);
            Assert.Contains(Role.Medico, roles);
            Assert.DoesNotContain(Role.Administrador, roles);
        }

        [Fact]
        public void IssueAccessToken_Medico_SoloMedico()
        {
            var token = Sut().IssueAccessToken(new UserInfo { Id = "med.cardio", DisplayName = "Medico", Roles = new List<string> { Role.Medico } });
            var roles = DecodeClaims(token).Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList();

            Assert.Equal(new[] { Role.Medico }, roles);
        }

        [Fact]
        public void IssueAccessToken_LlevaElRolPrincipalSinExpandir()
        {
            var token = Sut().IssueAccessToken(new UserInfo { Id = "jefe.cardio", DisplayName = "Jefe", Roles = new List<string> { Role.Jefe } });
            var principal = DecodeClaims(token).Single(c => c.Type == "role_principal").Value;

            Assert.Equal(Role.Jefe, principal);
        }

        [Fact]
        public void IssueAccessToken_LlevaLasEspecialidades()
        {
            var token = Sut().IssueAccessToken(new UserInfo
            {
                Id = "med.multi", DisplayName = "Medico Multi",
                Roles = new List<string> { Role.Medico },
                Specialties = new List<string> { "cardio", "neumo" }
            });
            var specialties = DecodeClaims(token).Where(c => c.Type == "specialty").Select(c => c.Value).ToList();

            Assert.Equal(new[] { "cardio", "neumo" }, specialties);
        }

        [Fact]
        public void IssueAccessToken_RolDesconocidoOHeredado_DegradaAMedico()
        {
            // p. ej. una cookie/token viejo con "Observador" (rol eliminado del sistema).
            var token = Sut().IssueAccessToken(new UserInfo { Id = "u1", DisplayName = "U1", Roles = new List<string> { "Observador" } });
            var roles = DecodeClaims(token).Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList();

            Assert.Equal(new[] { Role.Medico }, roles);
        }
    }
}
