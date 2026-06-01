using System;

namespace ChequeAR.Domain.Entities
{
    public class QuestionnaireResponseItem
    {
        public Guid Id { get; set; }
        public Guid ResponseId { get; set; }
        public string LinkId { get; set; }
        public string QuestionType { get; set; }
        public string ValueString { get; set; }
        public decimal? ValueDecimal { get; set; }
        public int? ValueInteger { get; set; }
        public DateTime? ValueDate { get; set; }
        public bool? ValueBoolean { get; set; }
        public string ValueCode { get; set; }
        public Guid? AttachmentId { get; set; }
        public bool WasPrefilled { get; set; }
    }
}
