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
            using (var conn = new OracleConnection(_fx.ConnectionStringRO))
            {
                conn.Open();
                using (var cmd = new OracleCommand(
                    "SELECT PRIVILEGE || ' ON ' || OWNER || '.' || TABLE_NAME FROM USER_TAB_PRIVS " +
                    "WHERE GRANTEE = USER AND PRIVILEGE NOT IN ('SELECT', 'READ') " +
                    "UNION ALL " +
                    "SELECT PRIVILEGE FROM SESSION_PRIVS " +
                    "WHERE PRIVILEGE LIKE '% ANY %' AND PRIVILEGE NOT LIKE 'SELECT %' AND PRIVILEGE NOT LIKE 'READ %'", conn))
                using (var reader = cmd.ExecuteReader())
                {
                    var encontrados = new System.Collections.Generic.List<string>();
                    while (reader.Read()) encontrados.Add(reader.GetString(0));
                    Assert.True(encontrados.Count == 0,
                        "CHEQUEAR_RO tiene privilegios de escritura: " + string.Join(", ", encontrados));
                }
            }
        }
    }
}
