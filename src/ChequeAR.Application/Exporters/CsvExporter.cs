using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ChequeAR.Application.Reports;

namespace ChequeAR.Application.Exporters
{
    public class CsvExporter
    {
        private static readonly char[] CharsQueRequierenComillas = { ';', '"', '\n', '\r' };

        public static Stream Export(ReportResult result)
        {
            var ms = new MemoryStream();
            var bom = new byte[] { 0xEF, 0xBB, 0xBF };
            ms.Write(bom, 0, bom.Length);
            using (var w = new StreamWriter(ms, new UTF8Encoding(false), 1024, leaveOpen: true))
            {
                if (result.Rows.Count == 0)
                {
                    w.Flush();
                    ms.Position = 0;
                    return ms;
                }

                var headers = new List<string>(result.Rows[0].Keys);
                w.WriteLine(string.Join(";", headers));
                foreach (var row in result.Rows)
                {
                    var fields = new List<string>(headers.Count);
                    foreach (var h in headers) fields.Add(Escape(row[h]));
                    w.WriteLine(string.Join(";", fields));
                }
                w.Flush();
            }
            ms.Position = 0;
            return ms;
        }

        private static string Escape(object value)
        {
            if (value == null) return string.Empty;
            string s;
            if (value is System.IFormattable f) s = f.ToString(null, CultureInfo.GetCultureInfo("es-AR"));
            else s = value.ToString();
            if (s.IndexOfAny(CharsQueRequierenComillas) >= 0)
                s = "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}
