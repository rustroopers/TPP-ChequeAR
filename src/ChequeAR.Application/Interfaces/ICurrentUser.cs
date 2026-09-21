using System.Collections.Generic;

namespace ChequeAR.Application.Interfaces
{
    /// <summary>
    /// Abstracción del usuario autenticado, para que Application pueda decidir autorización
    /// sin depender de System.Web/ClaimsPrincipal (LayerDependencyTests lo prohíbe). La
    /// implementación real vive en Api (HttpCurrentUser) y adapta el ClaimsPrincipal de MVC
    /// o Web API a esta interfaz.
    /// </summary>
    public interface ICurrentUser
    {
        bool IsAuthenticated { get; }
        string Id { get; }
        bool IsInRole(string role);

        /// <summary>Códigos de especialidad del claim "specialty". Vacía si no tiene ninguna.</summary>
        IReadOnlyList<string> Specialties { get; }

        /// <summary>
        /// El rol único y real del usuario (Administrador/Jefe/Medico), sin expandir por
        /// jerarquía -a diferencia de IsInRole, que sí considera "Jefe también es Medico".
        /// Para UI/lógica que necesita saber cuál es el rol propio del usuario.
        /// </summary>
        string Role { get; }
    }
}
