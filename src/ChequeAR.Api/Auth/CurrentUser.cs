using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Api.Auth
{
    public static class CurrentUser
    {
        public static string Id(ClaimsPrincipal principal) =>
            principal?.FindFirst("sub")?.Value
            ?? principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        /// <summary>Roles EXPANDIDOS por jerarquía (ver RoleHierarchy): un Jefe trae [Jefe, Medico].</summary>
        public static IReadOnlyList<string> Roles(ClaimsPrincipal principal) =>
            principal?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList() ?? new List<string>();

        public static IReadOnlyList<string> Specialties(ClaimsPrincipal principal) =>
            principal?.FindAll("specialty").Select(c => c.Value).ToList() ?? new List<string>();

        /// <summary>
        /// El rol único y real (sin expandir), para UI que necesita saber "cuál es SU rol"
        /// y no "el más bajo que también cumple" (p. ej. el modo lectura del editor de
        /// cuestionarios). Sale del claim "role_principal" (JwtIssuer/SessionCookie); si
        /// faltara -un principal externo que no pasó por ninguno de los dos- se deriva de
        /// los roles expandidos.
        /// </summary>
        public static string PrincipalRole(ClaimsPrincipal principal)
        {
            var claim = principal?.FindFirst("role_principal")?.Value;
            return !string.IsNullOrEmpty(claim) ? claim : RoleHierarchy.Principal(Roles(principal));
        }
    }
}
