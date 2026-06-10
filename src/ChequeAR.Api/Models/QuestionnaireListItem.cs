using System.Collections.Generic;

namespace ChequeAR.Api.Models
{
    public class QuestionnaireListItem
    {
        public long Id { get; set; }
        public string Title { get; set; }
        public string SpecialtyCode { get; set; }
        public string SpecialtyLabel { get; set; }
        public int Version { get; set; }
        public string Status { get; set; }
        public string UpdatedAt { get; set; }
        public string Author { get; set; }
    }

    public class QuestionnaireListViewModel
    {
        public List<QuestionnaireListItem> Items { get; set; } = new List<QuestionnaireListItem>();
        public string SearchTerm { get; set; }
        public string SpecialtyFilter { get; set; }
        public string StatusFilter { get; set; }
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    public class QuestionnaireDetailViewModel
    {
        public long Id { get; set; }
        public string Title { get; set; }
        public string SpecialtyCode { get; set; }
        public string SpecialtyLabel { get; set; }
        public int Version { get; set; }
        public string Status { get; set; }
        public string Author { get; set; }
        public string UpdatedAt { get; set; }
        public List<QuestionGroupView> Groups { get; set; } = new List<QuestionGroupView>();
    }

    public class QuestionGroupView
    {
        public string Title { get; set; }
        public List<QuestionView> Questions { get; set; } = new List<QuestionView>();
    }

    public class QuestionView
    {
        public string LinkId { get; set; }
        public string Text { get; set; }
        public string Type { get; set; }
        public bool Required { get; set; }
        public string Unit { get; set; }
        public List<string> Options { get; set; } = new List<string>();
    }

    public class QuestionnaireVersionView
    {
        public int Version { get; set; }
        public string Date { get; set; }
        public string Author { get; set; }
        public string Status { get; set; }
        public string ChangeNote { get; set; }
    }
}
