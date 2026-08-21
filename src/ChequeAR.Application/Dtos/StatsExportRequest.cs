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

        /// <summary>
        /// LinkId de pregunta categórica -> "bar" | "pie", tal como está elegido en pantalla.
        /// Solo aplica al Excel (los gráficos nativos); sin entrada, esa pregunta sale en
        /// barra. Word no tiene gráficos, así que este campo no lo afecta.
        /// </summary>
        public Dictionary<string, string> ChartTypes { get; set; }
    }
}
