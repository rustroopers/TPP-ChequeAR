using System;
using System.Collections.Generic;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Domain.Entities
{
    public class Question
    {
        public Guid Id { get; set; }
        public Guid QuestionnaireId { get; set; }
        public Guid? GroupId { get; set; }
        public string LinkId { get; set; }
        public string Text { get; set; }
        public QuestionType Type { get; set; }
        public bool Required { get; set; }
        public bool RepeatsAllowed { get; set; }
        public int OrderIndex { get; set; }
        public string ValidationJson { get; set; }
        public string EnableWhenJson { get; set; }
        public string CodingJson { get; set; }

        public virtual ICollection<AnswerOption> Options { get; set; } = new List<AnswerOption>();
    }
}
