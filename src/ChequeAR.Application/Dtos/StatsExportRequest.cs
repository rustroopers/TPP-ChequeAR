using System.Collections.Generic;

namespace ChequeAR.Application.Dtos
{
    public class StatsExportRequest
    {
        public ReportQuery Query { get; set; }

        /// <summary>Preguntas a incluir; null o vacío exporta todas.</summary>
        public List<string> LinkIds { get; set; }

        /// <summary>xlsx | docx</summary>
        public string Format { get; set; }
    }
}
