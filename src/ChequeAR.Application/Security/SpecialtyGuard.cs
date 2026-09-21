using ChequeAR.Application.Exceptions;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Application.Security
{
    /// <summary>
    /// Única fuente de la decisión "¿qué especialidades puede ver/tocar este usuario?".
    /// Se recalcula en cada acceso a <see cref="Scope"/> (no se cachea en el ctor): el DI de
    /// este proyecto (ServiceComposition) no tiene scope por request, así que una instancia
    /// puede sobrevivir a varias requests con usuarios distintos si se registrara mal.
    /// </summary>
    public sealed class SpecialtyGuard : ISpecialtyGuard
    {
        private readonly ICurrentUser _user;
        private readonly bool _enforce;

        /// <param name="enforce">
        /// Flag de despliegue (Authz:EnforceSpecialtyScope). En false, el guard nunca
        /// restringe: existe para poder desplegar el filtrado sin cambio de comportamiento
        /// mientras se puebla el espejo de usuarios, y revertir de inmediato si algo sale mal.
        /// </param>
        public SpecialtyGuard(ICurrentUser user, bool enforce)
        {
            _user = user;
            _enforce = enforce;
        }

        public SpecialtyScope Scope
        {
            get
            {
                if (!_enforce) return SpecialtyScope.Unrestricted();   // ocurrencia 1 de 2
                if (_user == null || !_user.IsAuthenticated) return SpecialtyScope.Nothing;
                if (_user.IsInRole(Role.Administrador)) return SpecialtyScope.Unrestricted();   // ocurrencia 2 de 2
                return SpecialtyScope.Limited(_user.Specialties);   // Jefe/Medico: restringidos
            }
        }

        public void EnsureAllows(string specialtyCode)
        {
            if (!Scope.Allows(specialtyCode)) throw new SpecialtyForbiddenException(specialtyCode);
        }
    }
}
