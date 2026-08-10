using System;

namespace ChequeAR.Domain.Entities
{
    public class QuestionnaireVersion
    {
        public Guid Id { get; set; }
        public Guid QuestionnaireId { get; set; }
        public int VersionNumber { get; set; }
        public string FhirJson { get; set; }
        public string PublishedBy { get; set; }
        public DateTime PublishedAt { get; set; }
        /// <summary>Qué cambió respecto de la versión anterior. Lo escribe quien publica.</summary>
        public string ChangeNote { get; set; }
    }
}
