using System;
using System.Collections.Generic;

namespace ChequeAR.Domain.Entities
{
    public class QuestionGroup
    {
        public Guid Id { get; set; }
        public Guid QuestionnaireId { get; set; }
        public Guid? ParentGroupId { get; set; }
        public string LinkId { get; set; }
        public string Title { get; set; }
        public int OrderIndex { get; set; }

        public virtual ICollection<Question> Questions { get; set; } = new List<Question>();
    }
}
