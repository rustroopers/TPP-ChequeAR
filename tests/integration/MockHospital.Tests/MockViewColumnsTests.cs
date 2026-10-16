using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Infrastructure.Oracle;
using Xunit;

namespace ChequeAR.Tests.MockHospital
{
    /// <summary>
    /// Las vistas del mock en Oracle y las que sirve MockOracleViewClient en memoria (lo que
    /// corre en Azure) tienen que exponer las mismas columnas: si divergen, algo que anda en
    /// un lado falla en el otro.
    /// </summary>
    [Collection("MockHospital")]
    // Necesita el mock cargado en Oracle; el CI lo excluye con --filter "Requires!=Oracle".
    [Trait("Requires", "Oracle")]
    public class MockViewColumnsTests
    {
        private static readonly string[] Views =
        {
            "VW_PACIENTE", "VW_EPISODIO", "VW_DIAGNOSTICO", "VW_OBSERVACION_LAB",
            "VW_SIGNOS_VITALES", "VW_MEDICACION", "VW_ALERGIA", "VW_PROFESIONAL",
            "VW_ESPECIALIDAD", "VW_TURNO"
        };

        private readonly MockHospitalFixture _fx;
        public MockViewColumnsTests(MockHospitalFixture fx) { _fx = fx; }

        public static IEnumerable<object[]> ViewNames() => Views.Select(v => new object[] { v });

        [Theory]
        [MemberData(nameof(ViewNames))]
        public async Task MockEnMemoria_MismasColumnasQueLaVista(string view)
        {
            var enOracle = new SortedSet<string>(_fx.CollectStrings(
                "SELECT COLUMN_NAME FROM ALL_TAB_COLUMNS WHERE OWNER='HOSPITAL_MOCK' AND TABLE_NAME='" + view + "'"),
                StringComparer.OrdinalIgnoreCase);
            var filas = await new MockOracleViewClient(Views).QueryAsync(view, filters: null);
            Assert.NotEmpty(filas);
            var enMemoria = new SortedSet<string>(filas[0].Keys, StringComparer.OrdinalIgnoreCase);

            Assert.Equal(enOracle, enMemoria);
        }
    }
}
