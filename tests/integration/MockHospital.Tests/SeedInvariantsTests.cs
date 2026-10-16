using System.Globalization;
using Xunit;

namespace ChequeAR.Tests.MockHospital
{
    /// <summary>
    /// FR-010..FR-013, FR-023, SC-006 — verifica conteos mínimos y edge cases
    /// del seed determinístico.
    /// </summary>
    [Collection("MockHospital")]
    // Necesita el Oracle XE del mock levantado. El CI de build/test lo excluye
    // con --filter "Requires!=Oracle"; no hay job que los corra: van a mano, contra un
    // Oracle con el esquema del mock (ver infra/mock-hospital/README.md).
    [Trait("Requires", "Oracle")]
    public class SeedInvariantsTests
    {
        private readonly MockHospitalFixture _fx;
        public SeedInvariantsTests(MockHospitalFixture fx) { _fx = fx; }

        [Fact]
        public void Pacientes_AlMenos50()
        {
            Assert.True(_fx.ScalarCount("VW_PACIENTE") >= 50);
        }

        [Fact]
        public void Episodios_AlMenos150()
        {
            Assert.True(_fx.ScalarCount("VW_EPISODIO") >= 150);
        }

        [Fact]
        public void Observaciones_AlMenos500()
        {
            Assert.True(_fx.ScalarCount("VW_OBSERVACION_LAB") >= 500);
        }

        [Fact]
        public void ExisteAlMenosUnPacienteSinEpisodios()
        {
            var count = _fx.ScalarString(
                "SELECT COUNT(*) FROM HOSPITAL_MOCK.VW_PACIENTE P " +
                "WHERE NOT EXISTS (SELECT 1 FROM HOSPITAL_MOCK.VW_EPISODIO E " +
                "WHERE E.ID_PACIENTE = P.ID_PACIENTE)");
            Assert.True(long.Parse(count, CultureInfo.InvariantCulture) >= 1);
        }

        [Fact]
        public void ExisteAlMenosUnPacienteCronico()
        {
            var count = _fx.ScalarString(
                "SELECT COUNT(*) FROM (" +
                " SELECT ID_PACIENTE FROM HOSPITAL_MOCK.VW_EPISODIO " +
                " GROUP BY ID_PACIENTE HAVING COUNT(*) >= 10)");
            Assert.True(long.Parse(count, CultureInfo.InvariantCulture) >= 1);
        }

        [Fact]
        public void ExisteAlMenosUnaObservacionTextual()
        {
            var count = _fx.ScalarString(
                "SELECT COUNT(*) FROM HOSPITAL_MOCK.VW_OBSERVACION_LAB " +
                "WHERE VALOR_TEXTO IS NOT NULL");
            Assert.True(long.Parse(count, CultureInfo.InvariantCulture) >= 1);
        }

        [Fact]
        public void ExisteAlMenosUnTurnoFuturo()
        {
            var count = _fx.ScalarString(
                "SELECT COUNT(*) FROM HOSPITAL_MOCK.VW_TURNO " +
                "WHERE FECHA_HORA > DATE '2026-06-25'");
            Assert.True(long.Parse(count, CultureInfo.InvariantCulture) >= 1);
        }

        [Fact]
        public void ExisteAlMenosUnEpisodioSinDxSecundario()
        {
            var count = _fx.ScalarString(
                "SELECT COUNT(*) FROM (" +
                " SELECT ID_EPISODIO FROM HOSPITAL_MOCK.VW_DIAGNOSTICO " +
                " GROUP BY ID_EPISODIO HAVING COUNT(*) = 1 AND MAX(ES_PRINCIPAL) = 1)");
            Assert.True(long.Parse(count, CultureInfo.InvariantCulture) >= 1);
        }

        [Fact]
        public void TodosLosPacientesEnEdadPediatrica()
        {
            var fueraRango = _fx.ScalarString(
                "SELECT COUNT(*) FROM HOSPITAL_MOCK.VW_PACIENTE " +
                "WHERE FECHA_NACIMIENTO < DATE '2026-06-25' - INTERVAL '18' YEAR " +
                "OR FECHA_NACIMIENTO > DATE '2026-06-25'");
            Assert.Equal(0L, long.Parse(fueraRango, CultureInfo.InvariantCulture));
        }
    }
}
