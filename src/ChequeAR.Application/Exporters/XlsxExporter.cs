using System.IO;
using ChequeAR.Application.Reports;

namespace ChequeAR.Application.Exporters
{
    /// <summary>MVP stub. Producción: usar ClosedXML para escribir un XLSX real.</summary>
    public class XlsxExporter
    {
        public Stream Export(ReportResult result)
        {
            // En MVP devolvemos un CSV con nombre xlsx para no introducir dependencia binaria.
            // En implementación real, reemplazar por ClosedXML XLWorkbook.
            return new CsvExporter().Export(result);
        }
    }
}
