using System;
using System.Collections.Generic;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Domain.Entities
{
    public class Questionnaire
    {
        public Guid Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public QuestionnaireStatus Status { get; set; }
        public string SpecialtyCode { get; set; }
        public string SpecialtyDisplay { get; set; }
        public Guid? CurrentVersionId { get; set; }
        public string CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool IsDeleted { get; set; }

        public virtual ICollection<QuestionnaireVersion> Versions { get; set; } = new List<QuestionnaireVersion>();
        public virtual ICollection<QuestionGroup> Groups { get; set; } = new List<QuestionGroup>();
        public virtual ICollection<Question> Questions { get; set; } = new List<Question>();

        public bool CanPublish()
        {
            return Status == QuestionnaireStatus.Draft && Questions != null && Questions.Count > 0;
        }
    }
}
