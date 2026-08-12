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
            Assert.Equal(string.Empty, ReadCsv(new CsvExporter().Export(Result())));
        }

        [Fact]
        public void Csv_EscribeHeadersYFilas_ConEscapadoYFormatoArgentino()
        {
            var csv = ReadCsv(new CsvExporter().Export(Result(
                new Dictionary<string, object> { { "Nombre", "Pérez; Juan" }, { "Peso", 3.5m }, { "Obs", null } })));

            var lines = csv.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            Assert.Equal("Nombre;Peso;Obs", lines[0]);
            // ';' fuerza comillas; decimal en es-AR usa coma; null -> vacío.
            Assert.Equal("\"Pérez; Juan\";3,5;", lines[1]);
        }

        [Fact]
        public void Csv_EscapaComillasDuplicandolas()
        {
            var csv = ReadCsv(new CsvExporter().Export(Result(
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
            using (var zip = new ZipArchive(new XlsxExporter().Export(Result()), ZipArchiveMode.Read))
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
            using (var zip = new ZipArchive(new XlsxExporter().Export(result), ZipArchiveMode.Read))
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
            using (var zip = new ZipArchive(new XlsxExporter().Export(Result(row)), ZipArchiveMode.Read))
                sheet = ReadEntry(zip, "xl/worksheets/sheet1.xml");

            Assert.Contains("r=\"AA1\"", sheet);   // header de la 27ª columna
            Assert.Contains("r=\"AA2\"", sheet);   // dato de la 27ª columna
        }
    }
}
