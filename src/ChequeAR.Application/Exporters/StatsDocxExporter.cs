using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using ChequeAR.Application.Reports;

namespace ChequeAR.Application.Exporters
{
    /// Vuelca el resumen estadístico (FR-008) a DOCX: igual que un XLSX, un .docx es un ZIP
    /// con partes XML (WordprocessingML en vez de SpreadsheetML). Se genera un documento
    /// editable -pensado para que el médico lo complete y comparta- sin dependencias externas.
    public class StatsDocxExporter
    {
        private static readonly CultureInfo Ar = CultureInfo.GetCultureInfo("es-AR");

        /// <param name="linkIds">Preguntas a incluir; null o vacío exporta todas.</param>
        public Stream Export(ReportStats stats, IReadOnlyCollection<string> linkIds = null)
        {
            var questions = Filtrar(stats.Questions, linkIds);

            var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                AddEntry(zip, "[Content_Types].xml", ContentTypes());
                AddEntry(zip, "_rels/.rels", RootRels());
                AddEntry(zip, "word/document.xml", Document(stats.Responses, questions));
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

        private static void AddEntry(ZipArchive zip, string path, string content)
        {
            var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
            using (var s = entry.Open())
            using (var w = new StreamWriter(s, new UTF8Encoding(false)))
            {
                w.Write(content);
            }
        }

        private static string ContentTypes() =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
            "</Types>";

        private static string RootRels() =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
            "</Relationships>";

        private static string Document(int responses, List<QuestionStats> questions)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>");

            Titulo(sb, "Estadísticas del reporte");
            Parrafo(sb, "Fichas de la cohorte: " + responses.ToString(Ar), bold: false);

            foreach (var q in questions)
            {
                Titulo2(sb, q.Text + " (" + q.Type + ")");
                Parrafo(sb, "Completitud: " + q.Answered + " / " + responses + " (" + q.CompletenessPct.ToString(Ar) + "%)");

                if (q.Kind == "numeric" && q.N > 0)
                {
                    Parrafo(sb, "n=" + q.N.ToString(Ar) +
                        "  Media=" + Num(q.Mean) + "  Mediana=" + Num(q.Median) + "  Desvío=" + Num(q.StdDev));
                    Parrafo(sb, "Mín=" + Num(q.Min) + "  Máx=" + Num(q.Max) +
                        "  P25=" + Num(q.P25) + "  P75=" + Num(q.P75));
                }
                else if (q.Kind == "categorical" && q.Categories != null)
                {
                    foreach (var c in q.Categories)
                        Parrafo(sb, "• " + c.Label + ": " + c.Count.ToString(Ar) + " (" + c.Pct.ToString(Ar) + "%)");
                }
            }

            sb.Append("</w:body></w:document>");
            return sb.ToString();
        }

        private static void Titulo(StringBuilder sb, string texto) =>
            sb.Append("<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t xml:space=\"preserve\">")
              .Append(Escape(texto)).Append("</w:t></w:r></w:p>");

        private static void Titulo2(StringBuilder sb, string texto) =>
            sb.Append("<w:p><w:pPr><w:pStyle w:val=\"Heading2\"/></w:pPr><w:r><w:t xml:space=\"preserve\">")
              .Append(Escape(texto)).Append("</w:t></w:r></w:p>");

        private static void Parrafo(StringBuilder sb, string texto, bool bold = false)
        {
            sb.Append("<w:p><w:r>");
            if (bold) sb.Append("<w:rPr><w:b/></w:rPr>");
            sb.Append("<w:t xml:space=\"preserve\">").Append(Escape(texto)).Append("</w:t></w:r></w:p>");
        }

        private static string Num(decimal? v) => v.HasValue ? v.Value.ToString(Ar) : "—";

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }
}
