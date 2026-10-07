using System;

namespace ChequeAR.Application.Dtos
{
    /// <summary>Un cuestionario reducido a lo que necesita un selector: id, título y si fue eliminado.</summary>
    public class QuestionnaireTitle
    {
        public Guid Id { get; set; }
        public string Title { get; set; }
        public bool IsDeleted { get; set; }
    }
}
