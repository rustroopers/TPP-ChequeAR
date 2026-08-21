using System.Collections.Generic;

namespace ChequeAR.Application.Dtos
{
    public class StatsExportRequest
    {
        public ReportQuery Query { get; set; }

        /// <summary>Preguntas a incluir; null o vacío exporta todas.</summary>
        public List<string> LinkIds { get; set; }

        /// <summary>
        /// LinkId de pregunta categórica -> "bar" | "pie", tal como está elegido en pantalla.
        /// Sin entrada, esa pregunta sale en barra.
        /// </summary>
        public Dictionary<string, string> ChartTypes { get; set; }
    }
}
