using System;
using System.Collections.Generic;
using ChequeAR.Application.Reports;
using Xunit;

namespace ChequeAR.Tests.Application
{
    public class ReportPivotTests
    {
        // La normalizacion del identificador (byte[16] -> Guid) es comportamiento del
        // driver, no del pivot: sus pruebas viven en MockHospital.Tests, que sí ve la capa
        // de infraestructura. Acá se asume ya normalizado, que es como llega.

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

        // 01:30 UTC del 23 son las 22:30 del 22 en Argentina.
        [Fact]
        public void ToWide_LaFechaSaleEnHoraDeArgentina()
        {
            var fila = Row(Guid.NewGuid(), "HC-1", "edad", "12");
            fila["AuthoredAt"] = new DateTime(2026, 7, 23, 1, 30, 0);

            var ancho = ReportPivot.ToWide(
                new ReportResult { Rows = new List<Dictionary<string, object>> { fila } },
                new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("edad", "Edad") });

            Assert.Equal("2026-07-22", ancho.Rows[0]["Fecha"]);
        }

        private static Dictionary<string, object> Row(Guid responseId, string hc, string linkId, string valueText)
        {
            return new Dictionary<string, object>
            {
                ["ResponseId"] = responseId,
                ["PatientHospitalId"] = hc,
                ["AuthoredAt"] = new DateTime(2026, 7, 22),
                ["LinkId"] = linkId,
                ["ValueText"] = valueText
            };
        }
    }
}
