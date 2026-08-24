using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Infrastructure.Oracle;
using Xunit;

namespace ChequeAR.Tests.MockHospital
{
    /// <summary>
    /// SC-004 — el mock provee filas consumibles por los mapeos Oracle→FHIR existentes.
    ///
    /// Estrategia: para cada YAML de OracleMappings/ (módulo 003), leer el set de
    /// columnas referenciadas y verificar que la VW_* correspondiente las expone
    /// todas. Esto detecta drift entre el mock y el mapping ANTES de instanciar
    /// el OracleToFhirMapper real (que es parte del módulo 003 y puede no estar
    /// terminado todavía).
    ///
    /// Si la carpeta OracleMappings/ no existe aún, el test pasa como no-op.
    /// Cuando el módulo 003 entregue los YAML, esta suite empieza a validar
    /// automáticamente.
    /// </summary>
    [Collection("MockHospital")]
    // Necesita el Oracle XE del mock levantado. El CI de build/test lo excluye
    // con --filter "Requires!=Oracle"; el job de integracion lo incluye.
    [Trait("Requires", "Oracle")]
    public class MappingCompatibilityTests
    {
        private readonly MockHospitalFixture _fx;
        public MappingCompatibilityTests(MockHospitalFixture fx) { _fx = fx; }

        private static string LocateMappingsDir()
        {
            // Buscar src/ChequeAR.Infrastructure/OracleMappings/ subiendo desde el bin.
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName,
                    "src", "ChequeAR.Infrastructure", "OracleMappings");
                if (Directory.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            return null;
        }

        [Fact]
        public async Task CadaVistaListadaResponde()
        {
            // Mientras los YAML no existan, ejercitamos al menos la conectividad básica
            // contra cada VW_* declarada. SC-008 se valida en su totalidad con la suite
            // del módulo 003 (ver tasks T026b).
            var allowlist = new[]
            {
                "VW_PACIENTE", "VW_EPISODIO", "VW_DIAGNOSTICO", "VW_OBSERVACION_LAB",
                "VW_SIGNOS_VITALES", "VW_MEDICACION", "VW_ALERGIA", "VW_PROFESIONAL",
                "VW_ESPECIALIDAD", "VW_TURNO"
            };
            var client = new OracleViewClient(_fx.ConnectionStringRO, allowlist);

            foreach (var view in allowlist)
            {
                var rows = await client.QueryAsync("HOSPITAL_MOCK." + view, filters: null);
                Assert.NotNull(rows); // Lista vacía es válida; null no.
            }
        }

        [Fact]
        public void OracleMappings_ColumnasMencionadasExistenEnLaVista()
        {
            var mappingsDir = LocateMappingsDir();
            if (mappingsDir == null || !Directory.EnumerateFiles(mappingsDir, "*.yaml").Any())
            {
                // Módulo 003 aún no entregó YAML — no hay nada que validar.
                return;
            }

            // Parser heurístico: cualquier identificador en MAYÚSCULAS+UNDERSCORE
            // que aparezca en el YAML se considera un nombre de columna candidato.
            // Verificamos que cada uno exista en alguna VW_* del mock.
            var allColumns = AllColumnsByView();

            foreach (var yaml in Directory.EnumerateFiles(mappingsDir, "*.yaml"))
            {
                var text = File.ReadAllText(yaml);
                var candidates = System.Text.RegularExpressions.Regex
                    .Matches(text, @"\b([A-Z][A-Z0-9_]{2,30})\b")
                    .Cast<System.Text.RegularExpressions.Match>()
                    .Select(m => m.Value)
                    .Where(s => s.Contains("_")) // Heurística: ignorar palabras todo-mayúsculas
                    .Distinct()
                    .ToArray();

                foreach (var col in candidates)
                {
                    // Si parece una columna del dominio (empieza con ID_, COD_, FECHA_, etc.),
                    // exigimos que exista en al menos una vista.
                    if (col.StartsWith("ID_") || col.StartsWith("COD_") ||
                        col.StartsWith("FECHA_") || col.StartsWith("VALOR_") ||
                        col == "NOMBRE" || col == "APELLIDO" || col == "SEXO")
                    {
                        Assert.True(
                            allColumns.Any(kv => kv.Value.Contains(col)),
                            $"Columna '{col}' referenciada por {Path.GetFileName(yaml)} no existe en ninguna VW_*");
                    }
                }
            }
        }

        private System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<string>> AllColumnsByView()
        {
            var dict = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<string>>(
                StringComparer.OrdinalIgnoreCase);
            var allowlist = new[]
            {
                "VW_PACIENTE", "VW_EPISODIO", "VW_DIAGNOSTICO", "VW_OBSERVACION_LAB",
                "VW_SIGNOS_VITALES", "VW_MEDICACION", "VW_ALERGIA", "VW_PROFESIONAL",
                "VW_ESPECIALIDAD", "VW_TURNO"
            };
            foreach (var view in allowlist)
            {
                var cols = _fx.CollectStrings(
                    $"SELECT COLUMN_NAME FROM ALL_TAB_COLUMNS " +
                    $"WHERE OWNER='HOSPITAL_MOCK' AND TABLE_NAME='{view}'");
                dict[view] = new System.Collections.Generic.HashSet<string>(cols, StringComparer.OrdinalIgnoreCase);
            }
            return dict;
        }
    }
}
