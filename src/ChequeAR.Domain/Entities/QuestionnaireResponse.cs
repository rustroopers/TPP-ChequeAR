using System;
using System.Collections.Generic;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Domain.Entities
{
    public class QuestionnaireResponse
    {
        public Guid Id { get; set; }
        public Guid QuestionnaireId { get; set; }
        public Guid QuestionnaireVersionId { get; set; }
        public string PatientHospitalId { get; set; }
        public string EncounterId { get; set; }
        public ResponseStatus Status { get; set; }
        public DateTime AuthoredAt { get; set; }
        public string AuthorUserId { get; set; }
        public string FhirJson { get; set; }
        public DateTime LastSavedAt { get; set; }
        public bool IsAutosave { get; set; }

        public virtual ICollection<QuestionnaireResponseItem> Items { get; set; } = new List<QuestionnaireResponseItem>();
    }
}
