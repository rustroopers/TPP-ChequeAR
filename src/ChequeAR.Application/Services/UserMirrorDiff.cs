using System;
using System.Collections.Generic;
using System.Linq;

namespace ChequeAR.Application.Services
{
    /// <summary>Qué hay que agregar/quitar en el espejo local para que quede igual a lo que informó el hospital.</summary>
    public sealed class MirrorDiff
    {
        public IReadOnlyList<string> RolesToAdd { get; set; } = new List<string>();
        public IReadOnlyList<string> RolesToRemove { get; set; } = new List<string>();
        public IReadOnlyList<string> SpecialtiesToAdd { get; set; } = new List<string>();
        public IReadOnlyList<string> SpecialtiesToRemove { get; set; } = new List<string>();
    }

    /// <summary>
    /// Calcula el diff entre lo que hay en el espejo local (UserRole/UserSpecialty) y lo que
    /// informó el hospital, para reconciliar sin pisar por las bravas (UpsertAsync con
    /// CurrentValues.SetValues no toca colecciones de navegación y NUNCA da de baja nada:
    /// esta es la lógica que faltaba).
    ///
    /// Vive en Application (no en el repositorio) para poder testearla sin tocar EF/Oracle.
    /// </summary>
    public static class UserMirrorDiff
    {
        /// <param name="currentSpecialties">Lo que hoy hay en UserSpecialty para este usuario.</param>
        /// <param name="incomingSpecialties">
        /// Lo que informó el hospital en este login/sync.
        /// null  = el hospital NO informó especialidades (respuesta parcial o degradada) -&gt;
        ///         no se toca el espejo: un hipo del servicio del hospital no puede vaciar
        ///         de golpe el acceso de todo el padrón.
        /// vacío = el hospital informó explícitamente "ninguna" -&gt; se limpia (revocación real).
        /// </param>
        public static MirrorDiff Compute(
            IEnumerable<string> currentRoles, IEnumerable<string> incomingRoles,
            IEnumerable<string> currentSpecialties, IEnumerable<string> incomingSpecialties)
        {
            var result = new MirrorDiff();

            if (incomingRoles != null)
            {
                var cur = Normalize(currentRoles);
                var inc = Normalize(incomingRoles);
                result.RolesToAdd = inc.Except(cur, StringComparer.OrdinalIgnoreCase).ToList();
                result.RolesToRemove = cur.Except(inc, StringComparer.OrdinalIgnoreCase).ToList();
            }

            if (incomingSpecialties != null)
            {
                var cur = Normalize(currentSpecialties);
                var inc = Normalize(incomingSpecialties);
                result.SpecialtiesToAdd = inc.Except(cur, StringComparer.OrdinalIgnoreCase).ToList();
                result.SpecialtiesToRemove = cur.Except(inc, StringComparer.OrdinalIgnoreCase).ToList();
            }

            return result;
        }

        private static List<string> Normalize(IEnumerable<string> values)
            => (values ?? Enumerable.Empty<string>())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
    }
}
