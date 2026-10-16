using System.Linq;
using ChequeAR.Application.Exceptions;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Application.Security
{
    /// <summary>
    /// Implementación única de IAccessGuard. Fail-closed: sin autenticar, o con un rol que
    /// ICurrentUser.IsInRole no reconoce para ninguno de los 3 roles conocidos, no otorga
    /// ningún permiso.
    ///
    /// Se recalcula en cada acceso (no cachea en el ctor), mismo motivo que SpecialtyGuard:
    /// el DI de este proyecto (ServiceComposition) no tiene scope por request.
    /// </summary>
    public sealed class AccessGuard : IAccessGuard
    {
        private readonly ICurrentUser _user;

        public AccessGuard(ICurrentUser user)
        {
            _user = user;
        }

        public bool Can(string permission)
        {
            if (_user == null || !_user.IsAuthenticated) return false;
            return RolePolicy.Allows(new[] { _user.Role }, permission);
        }

        public void EnsureCan(string permission)
        {
            if (!Can(permission)) throw new PermissionForbiddenException(permission);
        }
    }
}
