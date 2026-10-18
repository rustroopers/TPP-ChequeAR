using System;
using System.Collections.Generic;
using System.Linq;

namespace ChequeAR.Domain.Enums
{
    /// <summary>
    /// El dato guardado (UserRole) tiene un único rol por usuario. Esta clase es el único
    /// lugar donde ese rol único se traduce a la jerarquía Administrador > Jefe > Medico, para
    /// dos usos que sí la necesitan:
    ///   - Emitir claims (JwtIssuer, SessionCookie): "Jefe" expande a [Jefe, Medico], así que
    ///     [Authorize(Roles = Role.Medico)] sigue significando "Medico o superior".
    ///   - Reducir una lista heredada de roles (login/sync legacy) a "el más alto" para migrar
    ///     al modelo de rol único sin perder al usuario.
    /// RolePolicy y AccessGuard NO usan esto: la matriz de permisos ya es explícita por rol
    /// (ver RolePolicy), así que no hace falta -y sería un error- expandir ahí también.
    /// </summary>
    public static class RoleHierarchy
    {
        // Orden de mayor a menor. Único lugar del repo donde la jerarquía está escrita.
        private static readonly string[] Order = { Role.Administrador, Role.Jefe, Role.Medico };

        /// <summary>Rol dado + todos los que están por debajo en la jerarquía.</summary>
        public static IReadOnlyList<string> Expand(string role)
        {
            var idx = Array.IndexOf(Order, role);
            if (idx < 0) return new[] { Role.Medico }; // rol desconocido/heredado => el más restrictivo
            return Order.Skip(idx).ToArray();
        }

        /// <summary>
        /// El rol más alto presente en la lista. Vacía o sin ningún rol conocido => Medico
        /// (degradación segura: nunca se sube de nivel a un usuario por ambigüedad de datos).
        /// </summary>
        public static string Principal(IEnumerable<string> roles)
        {
            if (roles == null) return Role.Medico;
            var set = new HashSet<string>(roles, StringComparer.Ordinal);
            foreach (var r in Order)
                if (set.Contains(r)) return r;
            return Role.Medico;
        }

    }
}
