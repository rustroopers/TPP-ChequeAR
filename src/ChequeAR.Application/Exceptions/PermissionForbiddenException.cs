using System;

namespace ChequeAR.Application.Exceptions
{
    /// <summary>
    /// El rol del usuario actual no incluye el permiso pedido. Hermana de
    /// SpecialtyForbiddenException: esa niega por especialidad (qué recurso), ésta niega por
    /// rol (qué operación). La API la traduce a 403.
    /// </summary>
    public class PermissionForbiddenException : Exception
    {
        public PermissionForbiddenException(string permission)
            : base("No tenés permiso para '" + (permission ?? "(desconocido)") + "'.")
        {
            Permission = permission;
        }

        public string Permission { get; }
    }
}
