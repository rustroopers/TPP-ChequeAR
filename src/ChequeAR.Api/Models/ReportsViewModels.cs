using System.Collections.Generic;

namespace ChequeAR.Api.Models
{
    public class SavedReportItem
    {
        public long Id { get; set; }
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
}
