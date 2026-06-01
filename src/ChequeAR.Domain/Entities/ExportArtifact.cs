using System;

namespace ChequeAR.Domain.Entities
{
    public class ExportArtifact
    {
        public Guid Id { get; set; }
        public string Format { get; set; } // xlsx | csv
        public string StoragePath { get; set; }
        public long SizeBytes { get; set; }
        public string Sha256 { get; set; }
        public string CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
}
