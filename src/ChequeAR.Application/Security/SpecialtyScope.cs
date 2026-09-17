using System;
using System.Collections.Generic;
using System.Linq;

namespace ChequeAR.Application.Security
{
    /// <summary>
    /// El subconjunto de especialidades que un usuario puede ver/tocar. Fail-closed POR
    /// CONSTRUCCIÓN: no hay constructor público, así que no existe forma de terminar con un
    /// scope "todo permitido" por accidente (el bug clásico de un `IReadOnlyList allowed`
    /// donde `null` significa "sin restricción" — acá "sin restricción" requiere llamar
    /// explícitamente a <see cref="Unrestricted"/>, que debe aparecer exactamente 2 veces
    /// en todo el repo: acá y en SpecialtyGuard.Scope).
    /// </summary>
    public sealed class SpecialtyScope
    {
        private readonly HashSet<string> _codes;

        public bool IsUnrestricted { get; }
        public IReadOnlyCollection<string> Codes => _codes;

        private SpecialtyScope(bool unrestricted, IEnumerable<string> codes)
        {
            IsUnrestricted = unrestricted;
            _codes = new HashSet<string>(codes ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Sin restricción: ve y toca cualquier especialidad. Sólo Administrador, o el flag de despliegue apagado.</summary>
        public static SpecialtyScope Unrestricted() => new SpecialtyScope(true, null);

        /// <summary>Restringido a la unión de especialidades del usuario.</summary>
        public static SpecialtyScope Limited(IEnumerable<string> codes) => new SpecialtyScope(false, codes);

        /// <summary>Ninguna especialidad: no autenticado, o autenticado sin ninguna asignada.</summary>
        public static SpecialtyScope Nothing => new SpecialtyScope(false, null);

        public bool IsEmpty => !IsUnrestricted && _codes.Count == 0;

        /// <summary>
        /// ¿Este scope permite ver/tocar un recurso con este código? Un código null o vacío
        /// NUNCA pasa en un scope limitado: un recurso sin especialidad no es "de todos".
        /// </summary>
        public bool Allows(string specialtyCode)
            => IsUnrestricted || (!string.IsNullOrEmpty(specialtyCode) && _codes.Contains(specialtyCode));
    }
}
