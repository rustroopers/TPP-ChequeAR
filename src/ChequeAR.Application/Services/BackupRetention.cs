using System;
using System.Collections.Generic;
using System.Linq;

namespace ChequeAR.Application.Services
{
    /// <summary>
    /// Qué exports del deploy borrar de DATA_PUMP_DIR: el espacio es el de la base (20 GB en la
    /// Always Free) y cada deploy deja uno nuevo.
    /// </summary>
    public static class BackupRetention
    {
        public const string Prefix = "chequear_";
        public const int DefaultKeep = 3;

        /// <summary>
        /// Los archivos (.dmp y su .log) de los exports más viejos, conservando los últimos
        /// <paramref name="keep"/>. El nombre lleva la fecha (chequear_yyyyMMddHHmm), así que el
        /// orden alfabético es el cronológico. Otros archivos del directorio no se tocan.
        /// </summary>
        public static IReadOnlyList<string> ToDelete(IEnumerable<string> files, int keep)
        {
            var propios = (files ?? Enumerable.Empty<string>())
                .Where(f => f != null && f.StartsWith(Prefix, StringComparison.Ordinal)
                            && (f.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".log", StringComparison.OrdinalIgnoreCase)))
                .ToList();
            var exports = propios.Select(f => f.Substring(0, f.Length - 4))
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(n => n, StringComparer.Ordinal)
                .ToList();
            var viejos = new HashSet<string>(exports.Skip(Math.Max(keep, 1)), StringComparer.Ordinal);
            return propios.Where(f => viejos.Contains(f.Substring(0, f.Length - 4))).OrderBy(f => f, StringComparer.Ordinal).ToList();
        }
    }
}
