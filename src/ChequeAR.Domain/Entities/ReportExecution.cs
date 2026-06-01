using System;

namespace ChequeAR.Domain.Entities
{
    public class ReportExecution
    {
        public Guid Id { get; set; }
        public Guid? SavedReportId { get; set; }
        public string QueryJson { get; set; }
        public string ExecutedBy { get; set; }
        public DateTime ExecutedAt { get; set; }
        public int RowCount { get; set; }
        public int DurationMs { get; set; }
        public Guid? ArtifactId { get; set; }
        public string Status { get; set; }
    }
}
