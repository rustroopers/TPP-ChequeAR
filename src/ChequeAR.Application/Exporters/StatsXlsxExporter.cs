using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using ChequeAR.Application.Reports;

namespace ChequeAR.Application.Exporters
{
    /// Vuelca el resumen estadístico (FR-008) a XLSX: una hoja con el resumen numérico/
    /// completitud por pregunta y otra con el detalle de categorías. Mismo enfoque sin
    /// dependencias que XlsxExporter, reutilizando sus helpers de bajo nivel.
    public class StatsXlsxExporter
    {
        private static readonly CultureInfo Ar = CultureInfo.GetCultureInfo("es-AR");

        /// <param name="linkIds">Preguntas a incluir; null o vacío exporta todas.</param>
        public Stream Export(ReportStats stats, IReadOnlyCollection<string> linkIds = null)
        {
            var questions = Filtrar(stats.Questions, linkIds);

            var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                XlsxExporter.AddEntry(zip, "[Content_Types].xml", ContentTypes());
                XlsxExporter.AddEntry(zip, "_rels/.rels", RootRels());
                XlsxExporter.AddEntry(zip, "xl/workbook.xml", Workbook());
                XlsxExporter.AddEntry(zip, "xl/_rels/workbook.xml.rels", WorkbookRels());
                XlsxExporter.AddEntry(zip, "xl/worksheets/sheet1.xml", ResumenSheet(questions));
                XlsxExporter.AddEntry(zip, "xl/worksheets/sheet2.xml", CategoriasSheet(questions));
            }
            ms.Position = 0;
            return ms;
        }

        private static List<QuestionStats> Filtrar(List<QuestionStats> questions, IReadOnlyCollection<string> linkIds)
        {
            if (linkIds == null || linkIds.Count == 0) return questions;
            var set = new HashSet<string>(linkIds, System.StringComparer.OrdinalIgnoreCase);
            return questions.Where(q => set.Contains(q.LinkId)).ToList();
        }

        private static string ContentTypes() =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet2.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "</Types>";

        private static string RootRels() =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>";

        private static string Workbook() =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
            "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
            "<sheets>" +
            "<sheet name=\"Resumen\" sheetId=\"1\" r:id=\"rId1\"/>" +
            "<sheet name=\"Categorias\" sheetId=\"2\" r:id=\"rId2\"/>" +
            "</sheets></workbook>";

        private static string WorkbookRels() =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet2.xml\"/>" +
            "</Relationships>";

        private static readonly string[] ResumenHeaders =
        {
            "Pregunta", "Tipo", "Respondidas", "Faltantes", "Completitud %",
            "n", "Media", "Mediana", "Desvio", "Minimo", "Maximo", "P25", "P75"
        };

        private static string ResumenSheet(List<QuestionStats> questions)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");

            AppendRow(sb, 1, ResumenHeaders);
            int r = 2;
            foreach (var q in questions)
            {
                AppendRow(sb, r++, new[]
                {
                    q.Text, q.Type, q.Answered.ToString(Ar), q.Missing.ToString(Ar),
                    q.CompletenessPct.ToString(Ar),
                    q.Kind == "numeric" ? q.N.ToString(Ar) : "",
                    Num(q.Mean), Num(q.Median), Num(q.StdDev), Num(q.Min), Num(q.Max), Num(q.P25), Num(q.P75)
                });
            }

            sb.Append("</sheetData></worksheet>");
            return sb.ToString();
        }

        private static string CategoriasSheet(List<QuestionStats> questions)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");

            AppendRow(sb, 1, new[] { "Pregunta", "Categoria", "Cantidad", "%" });
            int r = 2;
            foreach (var q in questions.Where(q => q.Kind == "categorical" && q.Categories != null))
            {
                foreach (var c in q.Categories)
                    AppendRow(sb, r++, new[] { q.Text, c.Label, c.Count.ToString(Ar), c.Pct.ToString(Ar) });
            }

            sb.Append("</sheetData></worksheet>");
            return sb.ToString();
        }

        private static void AppendRow(StringBuilder sb, int rowIdx, IReadOnlyList<string> values)
        {
            sb.Append("<row r=\"").Append(rowIdx).Append("\">");
            for (int c = 0; c < values.Count; c++)
                sb.Append(XlsxExporter.TextCell(XlsxExporter.CellRef(c, rowIdx), values[c] ?? ""));
            sb.Append("</row>");
        }

        private static string Num(decimal? v) => v.HasValue ? v.Value.ToString(Ar) : "";
    }
}
