using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Infrastructure.Oracle;
using Xunit;

namespace ChequeAR.Tests.MockHospital
{
    public class AllowlistTests
    {
        private const string NoDatabase = "User Id=nadie;Password=nada;Data Source=localhost:1/NOEXISTE;";

        private static readonly string[] Allowlist = new[]
        {
            "VW_PACIENTE", "VW_EPISODIO", "VW_DIAGNOSTICO", "VW_OBSERVACION_LAB",
            "VW_SIGNOS_VITALES", "VW_MEDICACION", "VW_ALERGIA", "VW_PROFESIONAL",
            "VW_ESPECIALIDAD", "VW_TURNO"
        }.Select(v => "HOSPITAL_MOCK." + v).ToArray();

        [Fact]
        public async Task VistaNoListada_RechazadaPorAllowlist()
        {
            var client = new OracleViewClient(NoDatabase, Allowlist);
            await Assert.ThrowsAsync<ArgumentException>(() =>
                client.QueryAsync("HOSPITAL_MOCK.VW_INVENTADA", filters: null));
        }

        [Fact]
        public async Task TablaInterna_RechazadaPorAllowlist()
        {
            var client = new OracleViewClient(NoDatabase, Allowlist);
            // El nombre no empieza con VW_, no está en allowlist → rechazado SIN tocar la DB.
            await Assert.ThrowsAsync<ArgumentException>(() =>
                client.QueryAsync("HOSPITAL_MOCK.HOSP_PACIENTE", filters: null));
        }

        [Fact]
        public void AllowlistLoader_LeeArchivoYaml()
        {
            // Sanity check del loader: el archivo embebido contiene exactamente 10 vistas.
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Oracle", "allowlist.yaml");
            Assert.True(File.Exists(path), "No se encontró " + path);

            var loaded = AllowlistLoader.Load(path);
            Assert.Equal(10, loaded.Count);
            Assert.Contains("VW_PACIENTE", loaded);
            Assert.DoesNotContain("HOSP_PACIENTE", loaded);
        }

        [Theory]
        [InlineData("  - VW_PACIENTE\n  - vw_turno\n", 2)]
        [InlineData("allowed_views:\n  - VW_PACIENTE\n  - HOSP_PACIENTE\n", 3)]
        [InlineData("  - VW_PACIENTE\n  - VW PACIENTE\n", 2)]
        public void AllowlistLoader_EntradaInvalida_FallaConLaLinea(string contenido, int linea)
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, contenido);
                var ex = Assert.Throws<InvalidDataException>(() => AllowlistLoader.Load(path));
                Assert.Contains("línea " + linea, ex.Message);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
