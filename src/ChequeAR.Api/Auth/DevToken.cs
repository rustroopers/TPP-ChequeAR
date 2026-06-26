using System.Collections.Generic;
using System.Security.Principal;
using ChequeAR.Api.App_Start;
using ChequeAR.Application.Dtos;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Api.Auth
{
    /// Dev-only: emite un JWT con rol admin para que las islas del front puedan
    /// llamar a la API (que exige Bearer + rol) mientras el login del hospital no
    /// esté wired. Se expone en _Layout como meta tag SÓLO cuando la app corre en
    /// debug; en release el meta no se renderiza y el front no obtiene token.
    public static class DevToken
    {
        public static string IssueFor(IPrincipal user)
        {
            var jwt = ServiceComposition.Resolve<JwtIssuer>();
            if (jwt == null) return string.Empty;

            var name = (user != null && user.Identity != null && user.Identity.IsAuthenticated)
                ? user.Identity.Name
                : "dev";

            return jwt.IssueAccessToken(new UserInfo
            {
                Id = name,
                DisplayName = name,
                Roles = new List<string> { Role.Administrador, Role.Jefe, Role.Medico },
                Specialties = new List<string>()
            });
        }
    }
}
