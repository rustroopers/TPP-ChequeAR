using System;
using System.Collections.Generic;
using System.Linq;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Application.Security
{
    /// <summary>
    /// ÚNICO lugar del repo donde se escribe la matriz rol -&gt; permisos. Todo controller o
    /// servicio que necesite saber "¿puede este rol hacer X?" pasa por acá -nunca por un
    /// [Authorize(Roles = "Administrador,Jefe")] literal- así que cambiar la matriz es cambiar
    /// un solo archivo, y es exactamente lo que ChequeAR.Tests.Security.RolePolicyTests
    /// verifica celda por celda.
    ///
    /// Nota deliberada: NO expande por jerarquía acá (eso es RoleHierarchy, y es para claims).
    /// La matriz ya es explícita por rol: Jefe tiene sus propios permisos listados, no
    /// "los de Medico más algunos"; así un recorte futuro (p. ej. sacarle audit.read a Jefe)
    /// es un cambio de una lista, no de una regla de composición.
    /// </summary>
    public static class RolePolicy
    {
        private static readonly Dictionary<string, string[]> ByRole = new Dictionary<string, string[]>
        {
            [Role.Administrador] = new[]
            {
                Permissions.QuestionnaireRead, Permissions.QuestionnaireWrite, Permissions.QuestionnaireDelete,
                Permissions.EncounterWrite,
                Permissions.ReportRead, Permissions.ReportWrite,
                Permissions.AuditRead, Permissions.UserManage,
                Permissions.IntegrationRead, Permissions.TerminologyExpand
            },
            [Role.Jefe] = new[]
            {
                Permissions.QuestionnaireRead, Permissions.QuestionnaireWrite,
                // QuestionnaireDelete: NO. Borrar un cuestionario es destructivo e
                // irreversible; Jefe archiva ($archive), que cubre el uso diario.
                Permissions.EncounterWrite,
                Permissions.ReportRead, Permissions.ReportWrite,
                // AuditRead, UserManage: NO -- es exactamente lo que el requisito le saca a Jefe.
                Permissions.IntegrationRead, Permissions.TerminologyExpand
            },
            [Role.Medico] = new[]
            {
                Permissions.QuestionnaireRead, // sólo lectura: el `write` queda afuera a propósito
                Permissions.EncounterWrite,
                // ReportRead/ReportWrite, AuditRead, UserManage, IntegrationRead: NO.
                Permissions.TerminologyExpand // el renderer de fichas lo necesita para responder preguntas codificadas
            }
        };

        public static IReadOnlyCollection<string> PermissionsOf(string role)
            => (role != null && ByRole.TryGetValue(role, out var perms)) ? perms : Array.Empty<string>();

        /// <summary>¿Alguno de los roles del usuario otorga este permiso?</summary>
        public static bool Allows(IEnumerable<string> roles, string permission)
        {
            if (roles == null || string.IsNullOrEmpty(permission)) return false;
            return roles.Any(r => PermissionsOf(r).Contains(permission));
        }
    }
}
