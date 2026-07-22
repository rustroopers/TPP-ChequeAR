using System;
using System.Collections.Generic;
using ChequeAR.Application.Reports;
using Xunit;

namespace ChequeAR.Tests.Application
{
    public class ReportPivotTests
    {
        // El bug: ResponseId es RAW(16) en Oracle y ODP.NET lo devuelve como byte[].
        // Convert.ToString(byte[]) da "System.Byte[]" para TODAS las filas, asi que el
        // pivot agrupaba todas las fichas bajo la misma clave y devolvia UNA sola fila con
        // las respuestas de distintos pacientes mezcladas.
        [Fact]
        public void Normalize_SixteenByteArray_BecomesGuid()
        {
            var a = Guid.NewGuid();
            var b = Guid.NewGuid();

            var na = OracleValue.Normalize(a.ToByteArray());
            var nb = OracleValue.Normalize(b.ToByteArray());

            Assert.Equal(a, na);
            Assert.Equal(b, nb);
            Assert.NotEqual(Convert.ToString(na), Convert.ToString(nb));
        }

        // Un byte[] que no sea de 16 no es un Guid: no se toca.
        [Fact]
        public void Normalize_OtherValues_PassThrough()
        {
            Assert.Equal("texto", OracleValue.Normalize("texto"));
            Assert.Equal(42, OracleValue.Normalize(42));
            var otros = new byte[] { 1, 2, 3 };
            Assert.Same(otros, OracleValue.Normalize(otros));
        }

        // Con los ResponseId ya normalizados, cada ficha es una fila.
        [Fact]
        public void ToWide_OneRowPerResponse()
        {
            var r1 = Guid.NewGuid();
            var r2 = Guid.NewGuid();
            var largo = new ReportResult
            {
                Rows = new List<Dictionary<string, object>>
                {
                    Row(r1, "HC-1", "edad", "12"),
                    Row(r1, "HC-1", "subtipo", "Oligoarticular"),
                    Row(r2, "HC-2", "edad", "7"),
                    Row(r2, "HC-2", "subtipo", "Sistémica")
                }
            };

            var ancho = ReportPivot.ToWide(largo, new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("edad", "Edad"),
                new KeyValuePair<string, string>("subtipo", "Subtipo")
            });

            Assert.Equal(2, ancho.Total);
            Assert.Equal("HC-1", ancho.Rows[0]["HC"]);
            Assert.Equal("12", ancho.Rows[0]["Edad"]);
            Assert.Equal("Oligoarticular", ancho.Rows[0]["Subtipo"]);
            Assert.Equal("HC-2", ancho.Rows[1]["HC"]);
            Assert.Equal("7", ancho.Rows[1]["Edad"]);
        }

        private static Dictionary<string, object> Row(Guid responseId, string hc, string linkId, string valueText)
        {
            return new Dictionary<string, object>
            {
                ["ResponseId"] = OracleValue.Normalize(responseId.ToByteArray()),
                ["PatientHospitalId"] = hc,
                ["AuthoredAt"] = new DateTime(2026, 7, 22),
                ["LinkId"] = linkId,
                ["ValueText"] = valueText
            };
        }
    }
}
