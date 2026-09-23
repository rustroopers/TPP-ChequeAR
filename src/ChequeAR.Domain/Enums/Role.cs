using System.Linq;

namespace ChequeAR.Domain.Enums
{
    /// <summary>
    /// Los 3 roles del sistema. Cada usuario tiene EXACTAMENTE uno (invariante que valida
    /// UserService), no una acumulación: la jerarquía Administrador > Jefe > Medico se expresa
    /// aparte, en <see cref="RoleHierarchy"/>, al emitir claims -- nunca en el dato guardado.
    /// </summary>
    public static class Role
    {
        public const string Administrador = "Administrador";
        public const string Jefe = "Jefe";
        public const string Medico = "Medico";

        public static readonly string[] All = { Administrador, Jefe, Medico };

        public static bool IsKnown(string role) => All.Contains(role);
    }
}
