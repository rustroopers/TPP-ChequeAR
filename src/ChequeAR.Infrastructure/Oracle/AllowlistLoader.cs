using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ChequeAR.Infrastructure.Oracle
{
    /// <summary>
    /// Carga la allowlist de vistas autorizadas desde un archivo plano.
    /// No requiere YamlDotNet: parsea sólo líneas de la forma "- VW_X" o "VW_X".
    /// Cualquier otra cosa (encabezados, comentarios, líneas vacías) se ignora.
    ///
    /// Esto mantiene `allowlist.yaml` legible como YAML válido para humanos sin
    /// agregar dependencias al proyecto Infrastructure.
    /// </summary>
    public static class AllowlistLoader
    {
        // Sólo aceptamos identificadores Oracle. Esto es defensa en profundidad:
        // si alguien edita allowlist.yaml con basura, la cargamos limitada.
        private static readonly Regex ViewEntryPattern =
            new Regex(@"^\s*-?\s*([A-Z][A-Z0-9_]{0,29})\s*$", RegexOptions.Compiled);

        /// <summary>
        /// Lee el archivo y devuelve la lista de nombres de vistas autorizadas.
        /// </summary>
        public static IReadOnlyCollection<string> Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("path requerido", nameof(path));
            if (!File.Exists(path))
                throw new FileNotFoundException("Allowlist no encontrada", path);

            var views = new List<string>();
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var hash = line.IndexOf('#');
                if (hash >= 0) line = line.Substring(0, hash);
                if (string.IsNullOrWhiteSpace(line)) continue;

                var esEntrada = line.TrimStart().StartsWith("-", StringComparison.Ordinal);
                var match = ViewEntryPattern.Match(line);
                // Filtro adicional: las allowlist entries se esperan con prefijo VW_.
                // Si alguien pone "allowed_views" como entrada por error, esto lo descarta.
                var esVista = match.Success && match.Groups[1].Value.StartsWith("VW_", StringComparison.Ordinal);
                if (!esVista)
                {
                    if (esEntrada)
                        throw new InvalidDataException(
                            "Allowlist " + path + ", línea " + (i + 1) + ": '" + lines[i].Trim() + "' no es una vista válida (se espera `- VW_NOMBRE`).");
                    continue;
                }

                views.Add(match.Groups[1].Value);
            }

            if (views.Count == 0)
                throw new InvalidDataException(
                    "Allowlist vacía. Verificar formato: cada vista en una línea como `- VW_PACIENTE`.");

            return views.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }
}
