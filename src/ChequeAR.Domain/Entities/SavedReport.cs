using System;

namespace ChequeAR.Domain.Entities
{
    public class SavedReport
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Scope { get; set; } // private | specialty
        public string SpecialtyCode { get; set; }
        public Guid QuestionnaireId { get; set; }
        public string QueryJson { get; set; }
        public string CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
