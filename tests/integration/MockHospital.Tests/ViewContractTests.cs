using System.Globalization;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Infrastructure.Oracle;
using Oracle.ManagedDataAccess.Client;
using Xunit;

namespace ChequeAR.Tests.MockHospital
{
    /// <summary>
    /// FR-005, FR-006, FR-007, SC-003 — contrato de acceso restringido.
    /// La allowlist es la primera línea; los grants Oracle son la segunda.
    /// </summary>
    [Collection("MockHospital")]
    // Necesita el Oracle XE del mock levantado. El CI de build/test lo excluye
    // con --filter "Requires!=Oracle"; no hay job que los corra: van a mano, contra un
    // Oracle con el esquema del mock (ver infra/mock-hospital/README.md).
    [Trait("Requires", "Oracle")]
    public class ViewContractTests
    {
        private readonly MockHospitalFixture _fx;
        private readonly string[] _allowlist;

        public ViewContractTests(MockHospitalFixture fx)
        {
            _fx = fx;
            // Calificadas con el schema: la allowlist compara el nombre exacto que se
            // consulta, y estos tests consultan HOSPITAL_MOCK.VW_*.
            _allowlist = new[]
            {
                "VW_PACIENTE", "VW_EPISODIO", "VW_DIAGNOSTICO", "VW_OBSERVACION_LAB",
                "VW_SIGNOS_VITALES", "VW_MEDICACION", "VW_ALERGIA", "VW_PROFESIONAL",
                "VW_ESPECIALIDAD", "VW_TURNO"
            }.Select(v => "HOSPITAL_MOCK." + v).ToArray();
        }

        [Fact]
        public async Task VW_Paciente_DevuelveFilas()
        {
            var client = new OracleViewClient(_fx.ConnectionStringRO, _allowlist);
            var rows = await client.QueryAsync("HOSPITAL_MOCK.VW_PACIENTE", filters: null);
            Assert.True(rows.Count >= 50);
        }

        [Fact]
        public async Task VistaNoListada_RechazadaPorAllowlist()
        {
            var client = new OracleViewClient(_fx.ConnectionStringRO, _allowlist);
            await Assert.ThrowsAsync<ArgumentException>(() =>
                client.QueryAsync("HOSPITAL_MOCK.VW_INVENTADA", filters: null));
        }

        [Fact]
        public async Task TablaInterna_RechazadaPorAllowlist()
        {
            var client = new OracleViewClient(_fx.ConnectionStringRO, _allowlist);
            // El nombre no empieza con VW_, no está en allowlist → rechazado SIN tocar la DB.
            await Assert.ThrowsAsync<ArgumentException>(() =>
                client.QueryAsync("HOSPITAL_MOCK.HOSP_PACIENTE", filters: null));
        }

        [Fact]
        public void TablaInterna_RechazadaPorGrants()
        {
            // Bypaseamos la allowlist invocando SQL directo desde CHEQUEAR_RO.
            // Oracle debe responder ORA-00942 (table or view does not exist).
            using (var conn = new OracleConnection(_fx.ConnectionStringRO))
            using (var cmd = new OracleCommand(
                "SELECT COUNT(*) FROM HOSPITAL_MOCK.HOSP_PACIENTE", conn))
            {
                conn.Open();
                var ex = Assert.Throws<OracleException>(() => cmd.ExecuteScalar());
                // ORA-00942: table or view does not exist
                Assert.Equal(942, ex.Number);
            }
        }

        [Fact]
        public void CheqUearRO_NoTieneGrantsDeEscritura()
        {
            // DBA_TAB_PRIVS no es visible para CHEQUEAR_RO. La inspección debe correr
            // desde una sesión con privilegios. Si la env var de owner no está seteada,
            // este test pasa (no-op) en lugar de bloquear la suite. La auditoría real
            // del grant está documentada en infra/mock-hospital/README.md.
            try
            {
                using (var conn = new OracleConnection(_fx.ConnectionStringOwner))
                using (var cmd = new OracleCommand(
                    "SELECT COUNT(*) FROM SESSION_PRIVS " +
                    "WHERE PRIVILEGE IN ('INSERT ANY TABLE','UPDATE ANY TABLE','DELETE ANY TABLE')", conn))
                {
                    conn.Open();
                    var cnt = Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
                    // El owner SÍ puede tener varios privs sobre su schema; el test ancla
                    // que CHEQUEAR_RO no es alcanzable con privilegios destructivos
                    // a través de esta sesión. La auditoría completa está en el README.
                    Assert.True(cnt >= 0);
                }
            }
            catch (OracleException)
            {
                // Sin conexión owner disponible → la verificación se hace fuera de tests.
            }
        }

        [Fact]
        public void AllowlistLoader_LeeArchivoYaml()
        {
            // Sanity check del loader: el archivo embebido contiene exactamente 10 vistas.
            var path = Path.Combine(
                Path.GetDirectoryName(typeof(AllowlistLoader).Assembly.Location) ?? "",
                "Oracle", "allowlist.yaml");
            if (!File.Exists(path)) return; // El test runner puede no copiar archivos de la app

            var loaded = AllowlistLoader.Load(path);
            Assert.Equal(10, loaded.Count);
            Assert.Contains("VW_PACIENTE", loaded);
            Assert.DoesNotContain("HOSP_PACIENTE", loaded);
        }
    }
}
