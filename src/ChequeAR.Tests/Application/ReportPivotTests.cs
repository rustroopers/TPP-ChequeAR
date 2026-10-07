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

        [Fact]
        public void ToWide_OpcionMultiple_ConservaTodasLasOpcionesMarcadas()
        {
            var id = Guid.NewGuid();
            var ancho = ReportPivot.ToWide(
                new ReportResult
                {
                    Rows = new List<Dictionary<string, object>>
                    {
                        Row(id, "HC-1", "sintomas", "Fiebre"),
                        Row(id, "HC-1", "sintomas", "Rash")
                    }
                },
                new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("sintomas", "Síntomas") });

            Assert.Equal("Fiebre | Rash", ancho.Rows[0]["Síntomas"]);
        }

        [Fact]
        public void ToWide_PreguntasConElMismoTexto_NoSePisan()
        {
            var id = Guid.NewGuid();
            var ancho = ReportPivot.ToWide(
                new ReportResult
                {
                    Rows = new List<Dictionary<string, object>>
                    {
                        Row(id, "HC-1", "obs1", "primera"),
                        Row(id, "HC-1", "obs2", "segunda")
                    }
                },
                new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("obs1", "Observaciones"),
                    new KeyValuePair<string, string>("obs2", "Observaciones")
                });

            Assert.Equal("primera", ancho.Rows[0]["Observaciones"]);
            Assert.Equal("segunda", ancho.Rows[0]["Observaciones (obs2)"]);
        }

        [Fact]
        public void ToWide_PreguntaLlamadaComoUnaColumnaDeContexto_NoLaPisa()
        {
            var ancho = ReportPivot.ToWide(
                new ReportResult { Rows = new List<Dictionary<string, object>> { Row(Guid.NewGuid(), "HC-1", "f", "ayer") } },
                new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("f", "Fecha") });

            Assert.Equal("2026-07-21", ancho.Rows[0]["Fecha"]);
            Assert.Equal("ayer", ancho.Rows[0]["Fecha (f)"]);
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
