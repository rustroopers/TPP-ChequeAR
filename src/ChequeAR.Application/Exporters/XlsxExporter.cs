using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using ChequeAR.Application.Reports;

namespace ChequeAR.Application.Exporters
{
    /// Escribe un XLSX real (OpenXML SpreadsheetML) sin dependencias externas:
    /// un .xlsx es un ZIP con partes XML. Usamos strings "inline" para no manejar
    /// tabla de strings compartidos. Suficiente para exportes tabulares de reportes.
    public class XlsxExporter
    {
        private static readonly CultureInfo Ar = CultureInfo.GetCultureInfo("es-AR");

        public static Stream Export(ReportResult result)
        {
            var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                AddEntry(zip, "[Content_Types].xml", ContentTypes());
                AddEntry(zip, "_rels/.rels", RootRels());
                AddEntry(zip, "xl/workbook.xml", Workbook());
                AddEntry(zip, "xl/_rels/workbook.xml.rels", WorkbookRels());
                AddEntry(zip, "xl/worksheets/sheet1.xml", Sheet(result));
            }
            ms.Position = 0;
            return ms;
        }

        internal static void AddEntry(ZipArchive zip, string path, string content)
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
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
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
            "<sheets><sheet name=\"Reporte\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>";

        private static string WorkbookRels() =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
            "</Relationships>";

        private static string Sheet(ReportResult result)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");

            var headers = new List<string>();
            if (result.Rows.Count > 0) headers.AddRange(result.Rows[0].Keys);

            int rowIdx = 1;
            if (headers.Count > 0)
            {
                sb.Append("<row r=\"").Append(rowIdx).Append("\">");
                for (int c = 0; c < headers.Count; c++)
                    sb.Append(TextCell(CellRef(c, rowIdx), headers[c]));
                sb.Append("</row>");
                rowIdx++;
            }

            foreach (var row in result.Rows)
            {
                sb.Append("<row r=\"").Append(rowIdx).Append("\">");
                for (int c = 0; c < headers.Count; c++)
                {
                    row.TryGetValue(headers[c], out var val);
                    sb.Append(Cell(CellRef(c, rowIdx), val));
                }
                sb.Append("</row>");
                rowIdx++;
            }

            sb.Append("</sheetData></worksheet>");
            return sb.ToString();
        }

        private static string Cell(string reference, object value)
        {
            if (value == null) return "<c r=\"" + reference + "\"/>";
            if (value is bool b) return TextCell(reference, b ? "Sí" : "No");
            if (IsNumeric(value))
            {
                var num = Convert.ToString(value, CultureInfo.InvariantCulture);
                return "<c r=\"" + reference + "\"><v>" + num + "</v></c>";
            }
            if (value is DateTime dt) return TextCell(reference, dt.ToString("yyyy-MM-dd HH:mm", Ar));
            return TextCell(reference, value.ToString());
        }

        internal static string TextCell(string reference, string text) =>
            "<c r=\"" + reference + "\" t=\"inlineStr\"><is><t xml:space=\"preserve\">" + Escape(text) + "</t></is></c>";

        private static bool IsNumeric(object v) =>
            v is int || v is long || v is short || v is byte ||
            v is decimal || v is double || v is float;

        internal static string CellRef(int colZeroBased, int row)
        {
            var col = "";
            int n = colZeroBased;
            do { col = (char)('A' + (n % 26)) + col; n = n / 26 - 1; } while (n >= 0);
            return col + row;
        }

        internal static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return SinCaracteresInvalidos(s).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        /// <summary>
        /// XML 1.0 no admite caracteres de control (salvo tab, LF y CR), ni siquiera
        /// escapados: uno solo, pegado en un texto libre, alcanza para que Excel rechace el
        /// archivo entero.
        /// </summary>
        private static string SinCaracteresInvalidos(string s)
        {
            StringBuilder sb = null;
            for (int i = 0; i < s.Length; i++)
            {
                var c = s[i];
                bool valido;
                if (char.IsHighSurrogate(c)) valido = i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]);
                else if (char.IsLowSurrogate(c)) valido = i > 0 && char.IsHighSurrogate(s[i - 1]);
                else valido = System.Xml.XmlConvert.IsXmlChar(c);

                if (valido) { sb?.Append(c); continue; }
                if (sb == null) sb = new StringBuilder(s, 0, i, s.Length);
            }
            return sb == null ? s : sb.ToString();
        }
    }
}
