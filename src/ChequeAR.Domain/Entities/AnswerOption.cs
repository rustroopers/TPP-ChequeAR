using System;

namespace ChequeAR.Domain.Entities
{
    public class AnswerOption
    {
        public Guid Id { get; set; }
        public Guid QuestionId { get; set; }
        public string Value { get; set; }
        public string Display { get; set; }
        public string Code { get; set; }
        public int OrderIndex { get; set; }
    }
}
