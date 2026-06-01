using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace ChequeAR.Api.Auth
{
    public static class CurrentUser
    {
        public static string Id(ClaimsPrincipal principal) =>
            principal?.FindFirst("sub")?.Value
            ?? principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        public static IReadOnlyList<string> Roles(ClaimsPrincipal principal) =>
            principal?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList() ?? new List<string>();

        public static IReadOnlyList<string> Specialties(ClaimsPrincipal principal) =>
            principal?.FindAll("specialty").Select(c => c.Value).ToList() ?? new List<string>();
    }
}
