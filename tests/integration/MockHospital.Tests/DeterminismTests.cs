using Xunit;

namespace ChequeAR.Tests.MockHospital
{
    /// <summary>
    /// FR-009, FR-017, SC-002 — el seed es reproducible: con la misma semilla, nombres, códigos y
    /// valores salen iguales sin importar el día de la carga (sólo se corren las fechas).
    /// Si el seed cambia a propósito, recalcular los valores con la misma consulta.
    /// </summary>
    [Collection("MockHospital")]
    // Necesita el mock cargado en Oracle; el CI lo excluye con --filter "Requires!=Oracle".
    [Trait("Requires", "Oracle")]
    public class DeterminismTests
    {
        private readonly MockHospitalFixture _fx;
        public DeterminismTests(MockHospitalFixture fx) { _fx = fx; }

        // Suma de hashes por fila y no LISTAGG de la vista entera: con el dataset actual la
        // concatenación supera los 4000 bytes de VARCHAR2 (ORA-01489). Sin columnas de fecha.
        [Theory]
        [InlineData("VW_PACIENTE", "ID_PACIENTE || '|' || NOMBRE || '|' || APELLIDO || '|' || SEXO", "110971461700")]
        [InlineData("VW_EPISODIO", "ID_EPISODIO || '|' || ID_PACIENTE || '|' || TIPO || '|' || COD_ESPECIALIDAD", "336131541734")]
        [InlineData("VW_DIAGNOSTICO", "ID_DIAGNOSTICO || '|' || ID_EPISODIO || '|' || COD_CIE10 || '|' || ES_PRINCIPAL", "588979434430")]
        [InlineData("VW_OBSERVACION_LAB", "ID_OBSERVACION || '|' || ID_PACIENTE || '|' || COD_LOINC || '|' || TO_CHAR(VALOR_NUMERICO, 'TM9', 'NLS_NUMERIC_CHARACTERS=''.,''') || '|' || VALOR_TEXTO", "1514333918323")]
        public void Seed_DaSiempreElMismoDataset(string view, string columnExpr, string esperado)
        {
            var hash = _fx.ScalarString($"SELECT TO_CHAR(SUM(ORA_HASH({columnExpr}))) FROM HOSPITAL_MOCK.{view}");

            Assert.Equal(esperado, hash);
        }
    }
}
