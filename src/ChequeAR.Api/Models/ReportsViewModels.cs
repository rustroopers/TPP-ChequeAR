using System;
using System.Collections.Generic;

namespace ChequeAR.Api.Models
{
    public class SavedReportItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string QuestionnaireTitle { get; set; }
        public string Author { get; set; }
        public string LastUsedAt { get; set; }
    }

    public class ReportListViewModel
    {
        public List<SavedReportItem> Items { get; set; } = new List<SavedReportItem>();
    }

    public class ReportPreviewRow
    {
        public Dictionary<string, string> Cells { get; set; } = new Dictionary<string, string>();
    }

    public class ReportPreviewViewModel
    {
        public string ReportName { get; set; }
        public List<string> Columns { get; set; } = new List<string>();
        public List<ReportPreviewRow> Rows { get; set; } = new List<ReportPreviewRow>();
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    public class ReportStatsViewModel
    {
        public Guid? ReportId { get; set; }
        public string ReportName { get; set; }
        /// <summary>
        /// La consulta guardada, serializada en camelCase para que la isla la mande tal cual
        /// a /api/reports/stats. Vacía si el reporte no tiene consulta: la vista lo dice en
        /// vez de dibujar un gráfico sin datos.
        /// </summary>
        public string QueryJson { get; set; }
    }
}
