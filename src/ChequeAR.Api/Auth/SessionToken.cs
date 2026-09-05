using System.Security.Claims;
using System.Security.Principal;
using ChequeAR.Api.App_Start;
using ChequeAR.Application.Services;

namespace ChequeAR.Api.Auth
{
    /// Emite el JWT que _Layout estampa en el meta cq-token para que las islas Vue
    /// puedan llamar a la API, que exige Bearer.
    ///
    /// Reemplaza a DevToken, que firmaba rol Administrador hardcodeado para cualquiera
    /// y por eso solo podia renderizarse en debug. Aca los roles salen del principal
    /// (los del ticket de sesion, ver SessionCookie), asi que el token no puede dar mas
    /// permisos de los que ya tiene la persona logueada y es seguro emitirlo en release.
    public static class SessionToken
    {
        public static string IssueFor(IPrincipal user)
        {
            var principal = user as ClaimsPrincipal;
            if (principal == null || principal.Identity == null || !principal.Identity.IsAuthenticated)
                return string.Empty;

            var jwt = ServiceComposition.Resolve<JwtIssuer>();
            if (jwt == null) return string.Empty;

            var info = SessionCookie.ToUserInfo(principal);
            return info == null ? string.Empty : jwt.IssueAccessToken(info);
        }
    }
}
