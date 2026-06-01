using System;

namespace ChequeAR.Domain.Entities
{
    public class Attachment
    {
        public Guid Id { get; set; }
        public string OriginalFileName { get; set; }
        public string ContentType { get; set; }
        public long SizeBytes { get; set; }
        public string StoragePath { get; set; }
        public string UploadedBy { get; set; }
        public DateTime UploadedAt { get; set; }
        public string Hash { get; set; }
    }
}
