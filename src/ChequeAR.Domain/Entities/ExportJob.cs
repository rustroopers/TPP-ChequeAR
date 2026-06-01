using System;

namespace ChequeAR.Domain.Entities
{
    public class ExportJob
    {
        public Guid Id { get; set; }
        public string QueryJson { get; set; }
        public string Format { get; set; }
        public string RequestedBy { get; set; }
        public string Status { get; set; } // pending | running | completed | failed
        public decimal Progress { get; set; }
        public Guid? ArtifactId { get; set; }
        public string Error { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }
}
