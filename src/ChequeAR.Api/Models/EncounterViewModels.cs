using System.Collections.Generic;

namespace ChequeAR.Api.Models
{
    public class PatientSearchItem
    {
        public long Id { get; set; }
        public string HC { get; set; }
        public string FullName { get; set; }
        public string AgeLabel { get; set; }
        public string SpecialtyCode { get; set; }
        public string SpecialtyLabel { get; set; }
    }

    public class PatientSearchViewModel
    {
        public string Query { get; set; }
        public List<PatientSearchItem> Results { get; set; } = new List<PatientSearchItem>();
        public bool HasSearched { get; set; }
    }

    public class EncounterTimelineItem
    {
        public string Date { get; set; }
        public string QuestionnaireTitle { get; set; }
        public int QuestionnaireVersion { get; set; }
        public string Author { get; set; }
        public long EncounterId { get; set; }
        public string Status { get; set; }
    }

    public class PatientTimelineViewModel
    {
        public PatientSearchItem Patient { get; set; }
        public List<EncounterTimelineItem> Entries { get; set; } = new List<EncounterTimelineItem>();
    }

    public class EncounterReadonlyViewModel
    {
        public long EncounterId { get; set; }
        public PatientSearchItem Patient { get; set; }
        public string QuestionnaireTitle { get; set; }
        public int QuestionnaireVersion { get; set; }
        public string Author { get; set; }
        public string FinalizedAt { get; set; }
        public List<QuestionGroupAnswered> Groups { get; set; } = new List<QuestionGroupAnswered>();
    }

    public class QuestionGroupAnswered
    {
        public string Title { get; set; }
        public List<AnsweredQuestion> Questions { get; set; } = new List<AnsweredQuestion>();
    }

    public class AnsweredQuestion
    {
        public string Text { get; set; }
        public string Answer { get; set; }
        public string Unit { get; set; }
        public bool Prefilled { get; set; }
    }
}
