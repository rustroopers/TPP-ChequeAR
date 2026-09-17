using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Web;
using ChequeAR.Application.Interfaces;

namespace ChequeAR.Api.Auth
{
    /// <summary>
    /// Adaptador de ClaimsPrincipal a ICurrentUser (que vive en Application, sin conocer
    /// System.Web ni Web API). El principal se lee LAZY en cada acceso a una propiedad, no
    /// se cachea en el ctor: el DI de este proyecto (ServiceComposition) no tiene scope por
    /// request, así que una instancia larga podría sobrevivir a varias requests con usuarios
    /// distintos si se cacheara acá.
    ///
    /// Sirve a los dos pipelines con el mismo código: en MVC, HttpContext.Current.User trae
    /// el principal reconstruido por Global.asax (cookie Forms); en Web API, el filtro JWT
    /// deja el principal en HttpContext.Current.User también (OWIN lo sincroniza), y como
    /// red de contención se cae a Thread.CurrentPrincipal.
    /// </summary>
    public sealed class HttpCurrentUser : ICurrentUser
    {
        private static ClaimsPrincipal Principal =>
            (HttpContext.Current?.User as ClaimsPrincipal) ?? (Thread.CurrentPrincipal as ClaimsPrincipal);

        public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
        public string Id => CurrentUser.Id(Principal);
        public bool IsInRole(string role) => Principal?.IsInRole(role) == true;
        public IReadOnlyList<string> Specialties => CurrentUser.Specialties(Principal);
    }
}
