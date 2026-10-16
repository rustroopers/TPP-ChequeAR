using System.Globalization;
using System;
using ChequeAR.Infrastructure.Oracle;
using Xunit;

namespace ChequeAR.Tests.MockHospital
{
    /// <summary>
    /// Normalización de valores que devuelve el driver de Oracle.
    ///
    /// Vive en este proyecto y no en ChequeAR.Tests porque es comportamiento de
    /// INFRAESTRUCTURA: cómo un driver concreto representa un identificador. Y
    /// ChequeAR.Tests no ve la capa de infraestructura a propósito — esa barrera es lo que
    /// hace verificable la afirmación de aislamiento del documento de arquitectura.
    ///
    /// Estas pruebas venían de ReportPivotTests, donde estaban mezcladas con la lógica del
    /// pivot. Al mover la normalización a su capa, las pruebas la siguieron.
    /// </summary>
    public class OracleValueNormalizationTests
    {
        /// <summary>
        /// El bug original: el identificador de ficha es RAW(16) y el driver lo devuelve como
        /// byte[]. Convert.ToString(byte[]) da "System.Byte[]" para TODAS las filas, así que
        /// el pivot agrupaba todas las fichas bajo la misma clave y devolvía UNA sola fila con
        /// las respuestas de distintos pacientes mezcladas. En el Excel se exportaba ese
        /// literal.
        /// </summary>
        [Fact]
        public void BinarioDeDieciseisBytes_VuelveAserIdentificador()
        {
            var a = Guid.NewGuid();
            var b = Guid.NewGuid();

            var na = OracleReportQueryRunner.Normalizar(a.ToByteArray());
            var nb = OracleReportQueryRunner.Normalizar(b.ToByteArray());

            Assert.Equal(a, na);
            Assert.Equal(b, nb);
            Assert.NotEqual(Convert.ToString(na, CultureInfo.InvariantCulture), Convert.ToString(nb, CultureInfo.InvariantCulture));
        }

        [Fact]
        public void OtrosValores_PasanSinTocarse()
        {
            Assert.Equal("texto", OracleReportQueryRunner.Normalizar("texto"));
            Assert.Equal(42, OracleReportQueryRunner.Normalizar(42));

            // Un byte[] que no sea de 16 no es un identificador: no se toca.
            var otros = new byte[] { 1, 2, 3 };
            Assert.Same(otros, OracleReportQueryRunner.Normalizar(otros));
        }
    }
}
