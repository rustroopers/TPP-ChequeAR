namespace ChequeAR.Application.Security
{
    /// <summary>
    /// "¿Puede el usuario actual hacer X?", el eje ROL de la autorización (paralelo a
    /// ISpecialtyGuard, que es el eje especialidad). Una operación protegida normalmente
    /// exige ambos: EnsureCan(permiso) + EnsureAllows(especialidad-del-recurso).
    /// </summary>
    public interface IAccessGuard
    {
        /// <summary>Para decisiones de UI (mostrar/ocultar un botón). Nunca lanza.</summary>
        bool Can(string permission);

        /// <summary>Lanza PermissionForbiddenException si el usuario actual no tiene el permiso.</summary>
        void EnsureCan(string permission);
    }
}
