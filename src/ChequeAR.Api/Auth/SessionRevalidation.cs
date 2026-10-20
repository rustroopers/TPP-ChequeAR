using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using ChequeAR.Api.App_Start;
using ChequeAR.Application.Security;
using ChequeAR.Application.Services;

namespace ChequeAR.Api.Auth
{
    /// <summary>Revalida la identidad de la cookie o del JWT contra <see cref="SessionValidator"/>.</summary>
    public static class SessionRevalidation
    {
        public static Task<bool> IsStillValidAsync(ClaimsIdentity identity)
        {
            if (identity == null || !identity.IsAuthenticated) return Task.FromResult(false);

            var validator = ServiceComposition.Container?.GetInstance<SessionValidator>();
            // Todavía no terminó Application_Start.
            if (validator == null) return Task.FromResult(false);

            var userId = identity.FindFirst("sub")?.Value
                         ?? identity.FindFirst(ClaimTypes.NameIdentifier)?.Value
                         ?? identity.Name;
            return validator.IsValidAsync(
                userId,
                identity.FindFirst(JwtIssuer.SecurityStampClaim)?.Value,
                identity.FindFirst("role_principal")?.Value,
                identity.FindAll("specialty").Select(c => c.Value));
        }

        public static void Invalidate(string userId)
        {
            ServiceComposition.Container?.GetInstance<SessionValidator>()?.Invalidate(userId);
        }
    }
}
