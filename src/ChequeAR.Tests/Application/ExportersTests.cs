using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using ChequeAR.Application.Exporters;
using ChequeAR.Application.Reports;
using Xunit;

namespace ChequeAR.Tests.Application
{
    /// <summary>
    /// Exportadores de reportes/auditoría a CSV y XLSX. Se verifica el contenido real (no sólo
    /// que "no explote"): headers, escapado, formato numérico/fecha es-AR y, en XLSX, que el
    /// paquete sea un ZIP OpenXML válido con las partes esperadas.
    /// </summary>
    public class ExportersTests
    {
        private static ReportResult Result(params Dictionary<string, object>[] rows)
            => new ReportResult { Total = rows.Length, Rows = rows.ToList() };

        private static string ReadCsv(Stream s)
        {
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                var bytes = ms.ToArray();
                // Saltear el BOM UTF-8 (3 bytes) que el exportador escribe para que Excel abra bien.
                Assert.True(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            }
        }

        // ---- CSV ----

        [Fact]
        public void Csv_SinFilas_SoloBom()
        {
            Assert.Equal(string.Empty, ReadCsv(CsvExporter.Export(Result())));
        }

        [Fact]
        public void Csv_EscribeHeadersYFilas_ConEscapadoYFormatoArgentino()
        {
            var csv = ReadCsv(CsvExporter.Export(Result(
                new Dictionary<string, object> { { "Nombre", "Pérez; Juan" }, { "Peso", 3.5m }, { "Obs", null } })));

            var lines = csv.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            Assert.Equal("Nombre;Peso;Obs", lines[0]);
            // ';' fuerza comillas; decimal en es-AR usa coma; null -> vacío.
            Assert.Equal("\"Pérez; Juan\";3,5;", lines[1]);
        }

        [Fact]
        public void Csv_EscapaComillasDuplicandolas()
        {
            var csv = ReadCsv(CsvExporter.Export(Result(
                new Dictionary<string, object> { { "C", "dice \"hola\"" } })));

            var lines = csv.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            Assert.Equal("\"dice \"\"hola\"\"\"", lines[1]);
        }

        // ---- XLSX ----

        private static Dictionary<string, ZipArchiveEntry> Entries(ZipArchive zip)
            => zip.Entries.ToDictionary(e => e.FullName, e => e);

        private static string ReadEntry(ZipArchive zip, string path)
        {
            using (var r = new StreamReader(zip.GetEntry(path).Open(), Encoding.UTF8))
                return r.ReadToEnd();
        }

        [Fact]
        public void Xlsx_EsUnZipOpenXmlConLasPartesEsperadas()
        {
            using (var zip = new ZipArchive(XlsxExporter.Export(Result()), ZipArchiveMode.Read))
            {
                var entries = Entries(zip);
                Assert.Contains("[Content_Types].xml", entries.Keys);
                Assert.Contains("_rels/.rels", entries.Keys);
                Assert.Contains("xl/workbook.xml", entries.Keys);
                Assert.Contains("xl/_rels/workbook.xml.rels", entries.Keys);
                Assert.Contains("xl/worksheets/sheet1.xml", entries.Keys);
            }
        }

        [Fact]
        public void Xlsx_EscribeCadaTipoDeCeldaEnSuFormato()
        {
            var result = Result(new Dictionary<string, object>
            {
                { "Texto", "a<b>&c" },
                { "Entero", 42 },
                { "Decimal", 3.5m },
                { "Si", true },
                { "No", false },
                { "Fecha", new DateTime(2026, 6, 1, 14, 30, 0) },
                { "Nulo", null }
            });

            string sheet;
            using (var zip = new ZipArchive(XlsxExporter.Export(result), ZipArchiveMode.Read))
                sheet = ReadEntry(zip, "xl/worksheets/sheet1.xml");

            Assert.Contains("a&lt;b&gt;&amp;c", sheet);          // texto escapado
            Assert.Contains("<v>42</v>", sheet);                 // entero numérico
            Assert.Contains("<v>3.5</v>", sheet);                // decimal en InvariantCulture
            Assert.Contains(">Sí<", sheet);                      // bool true
            Assert.Contains(">No<", sheet);                      // bool false
            Assert.Contains("2026-06-01 14:30", sheet);          // fecha es-AR
            Assert.Contains("<c r=\"G2\"/>", sheet);             // celda nula = vacía (col G, fila 2)
        }

        [Fact]
        public void Xlsx_ReferenciaDeColumna_PasaDeZAAaDobleLetra()
        {
            // 27 columnas: la 27 (índice 26) obliga al bucle de CellRef a iterar dos veces -> "AA".
            var row = new Dictionary<string, object>();
            for (int i = 0; i < 27; i++) row["c" + i] = i;

            string sheet;
            using (var zip = new ZipArchive(XlsxExporter.Export(Result(row)), ZipArchiveMode.Read))
                sheet = ReadEntry(zip, "xl/worksheets/sheet1.xml");

            Assert.Contains("r=\"AA1\"", sheet);   // header de la 27ª columna
            Assert.Contains("r=\"AA2\"", sheet);   // dato de la 27ª columna
        }

        // ---- StatsXlsxExporter ----

        private static ReportStats StatsConDosPreguntas() => new ReportStats
        {
            Responses = 10,
            Questions = new List<QuestionStats>
            {
                new QuestionStats
                {
                    LinkId = "peso", Text = "Peso", Type = "Decimal", Kind = "numeric",
                    Answered = 8, Missing = 2, CompletenessPct = 80,
                    N = 8, Mean = 12.5m, Median = 12m, StdDev = 1.1m, Min = 10m, Max = 15m, P25 = 11m, P75 = 13m
                },
                new QuestionStats
                {
                    LinkId = "dolor", Text = "¿Dolor?", Type = "Boolean", Kind = "categorical",
                    Answered = 10, Missing = 0, CompletenessPct = 100,
                    N = 10,
                    Categories = new List<CategoryCount>
                    {
                        new CategoryCount { Label = "Sí", Count = 7, Pct = 70 },
                        new CategoryCount { Label = "No", Count = 3, Pct = 30 }
                    }
                }
            }
        };

        [Fact]
        public void StatsXlsx_EscribeResumenYCategoriasDeAmbasPreguntas()
        {
            string resumen, categorias;
            using (var zip = new ZipArchive(StatsXlsxExporter.Export(StatsConDosPreguntas()), ZipArchiveMode.Read))
            {
                resumen = ReadEntry(zip, "xl/worksheets/sheet1.xml");
                categorias = ReadEntry(zip, "xl/worksheets/sheet2.xml");
            }

            Assert.Contains("Peso", resumen);
            Assert.Contains("12,5", resumen); // media en formato es-AR (coma decimal)
            Assert.Contains("¿Dolor?", categorias);
            Assert.Contains("Sí", categorias);
            Assert.Contains("70", categorias);
        }

        [Fact]
        public void StatsXlsx_FiltraPorLinkIdsSeleccionados()
        {
            string resumen;
            using (var zip = new ZipArchive(StatsXlsxExporter.Export(StatsConDosPreguntas(), new[] { "peso" }), ZipArchiveMode.Read))
                resumen = ReadEntry(zip, "xl/worksheets/sheet1.xml");

            Assert.Contains("Peso", resumen);
            Assert.DoesNotContain("¿Dolor?", resumen);
        }

        [Fact]
        public void StatsXlsx_EscribeHojaDeHistogramasConLosBins()
        {
            var stats = StatsConDosPreguntas();
            stats.Questions[0].Histogram = new List<HistogramBin>
            {
                new HistogramBin { From = 10m, To = 12m, Count = 3 },
                new HistogramBin { From = 12m, To = 15m, Count = 5 }
            };

            string histogramas;
            using (var zip = new ZipArchive(StatsXlsxExporter.Export(stats), ZipArchiveMode.Read))
                histogramas = ReadEntry(zip, "xl/worksheets/sheet3.xml");

            Assert.Contains("Peso", histogramas);
            Assert.Contains("10-12", histogramas);
            Assert.Contains("12-15", histogramas);
        }

        [Fact]
        public void StatsXlsx_AgregaGraficosNativosParaCategoricasEHistogramas()
        {
            var stats = StatsConDosPreguntas();
            stats.Questions[0].Histogram = new List<HistogramBin>
            {
                new HistogramBin { From = 10m, To = 12m, Count = 3 },
                new HistogramBin { From = 12m, To = 15m, Count = 5 }
            };

            using (var zip = new ZipArchive(StatsXlsxExporter.Export(stats), ZipArchiveMode.Read))
            {
                var entries = zip.Entries.Select(e => e.FullName).ToList();
                Assert.Contains("xl/worksheets/sheet4.xml", entries);
                Assert.Contains("xl/drawings/drawing1.xml", entries);
                Assert.Contains("xl/charts/chart1.xml", entries); // barra de "dolor" (default sin ChartTypes)
                Assert.Contains("xl/charts/chart2.xml", entries); // barra del histograma de "peso"

                var categorica = ReadEntry(zip, "xl/charts/chart1.xml");
                Assert.Contains("c:barChart", categorica); // sin indicacion explicita, por defecto es barra
                Assert.Contains("Categorias", categorica);
                Assert.Contains("c:showPercent val=\"1\"", categorica); // cantidad + % como etiqueta

                var bar = ReadEntry(zip, "xl/charts/chart2.xml");
                Assert.Contains("c:barChart", bar);
                Assert.Contains("Histogramas", bar);
            }
        }

        [Fact]
        public void StatsXlsx_UsaTortaCuandoElChartTypeLoIndica()
        {
            var stats = StatsConDosPreguntas();
            var chartTypes = new Dictionary<string, string> { { "dolor", "pie" } };

            using (var zip = new ZipArchive(StatsXlsxExporter.Export(stats, null, chartTypes), ZipArchiveMode.Read))
            {
                var chart = ReadEntry(zip, "xl/charts/chart1.xml");
                Assert.Contains("c:pieChart", chart);
            }
        }

        [Fact]
        public void StatsXlsx_ResumenIncluyeCuestionarioPeriodoYFiltros()
        {
            var stats = StatsConDosPreguntas();
            stats.QuestionnaireTitle = "Control de embarazo";
            stats.DateFrom = new DateTime(2026, 1, 1);
            stats.DateTo = new DateTime(2026, 6, 30);
            stats.FiltersSummary = "Peso >= 10";

            string resumen;
            using (var zip = new ZipArchive(StatsXlsxExporter.Export(stats), ZipArchiveMode.Read))
                resumen = ReadEntry(zip, "xl/worksheets/sheet1.xml");

            Assert.Contains("Control de embarazo", resumen);
            Assert.Contains("01/01/2026", resumen);
            Assert.Contains("30/06/2026", resumen);
            Assert.Contains("Peso &gt;= 10", resumen); // escapado XML de '>='
        }

        [Fact]
        public void StatsXlsx_DistribuyeGraficosEnGrillaDeDosColumnas()
        {
            var stats = StatsConDosPreguntas();
            stats.Questions.Add(new QuestionStats
            {
                LinkId = "otraCategorica", Text = "Fumador?", Type = "Boolean", Kind = "categorical",
                Categories = new List<CategoryCount> { new CategoryCount { Label = "Sí", Count = 1, Pct = 100 } }
            });

            using (var zip = new ZipArchive(StatsXlsxExporter.Export(stats), ZipArchiveMode.Read))
            {
                var drawing = ReadEntry(zip, "xl/drawings/drawing1.xml");
                // El primer grafico arranca en col 0, el segundo (misma fila de la grilla) en col 9.
                Assert.Contains("<xdr:col>0</xdr:col>", drawing);
                Assert.Contains("<xdr:col>9</xdr:col>", drawing);
            }
        }

        [Fact]
        public void StatsXlsx_SinCategoricasNiHistogramas_NoAgregaHojaDeGraficos()
        {
            var stats = StatsConDosPreguntas();
            stats.Questions.RemoveAll(q => q.Kind == "categorical"); // solo queda "peso" sin Histogram

            using (var zip = new ZipArchive(StatsXlsxExporter.Export(stats), ZipArchiveMode.Read))
            {
                var entries = zip.Entries.Select(e => e.FullName).ToList();
                Assert.DoesNotContain("xl/worksheets/sheet4.xml", entries);
                Assert.DoesNotContain("xl/drawings/drawing1.xml", entries);
            }
        }

    }
}
