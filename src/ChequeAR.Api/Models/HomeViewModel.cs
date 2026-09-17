using System.Collections.Generic;

namespace ChequeAR.Api.Models
{
    public class HomeViewModel
    {
        public string UserGreeting { get; set; }
        public bool IsAdmin { get; set; }
        public List<RecentEncounter> RecentEncounters { get; set; } = new List<RecentEncounter>();
        public KpiSummary Kpis { get; set; }
    }

    public class RecentEncounter
    {
        public string PatientName { get; set; }
        public string QuestionnaireTitle { get; set; }
        // El código es para el modificador del chip (cq-chip--cardio); lo que se muestra es
        // siempre el label. Misma convención que QuestionnaireListItem y los view models de
        // encuentros: la UI nunca imprime el código crudo.
        public string SpecialtyCode { get; set; }
        public string SpecialtyLabel { get; set; }
        public string SpecialtyColor { get; set; }
        public string Status { get; set; }
        public string UpdatedAt { get; set; }
        public string OpenUrl { get; set; }
    }

    public class KpiSummary
    {
        public int Questionnaires { get; set; }
        public int Encounters { get; set; }
        public int Users { get; set; }
        public int Specialties { get; set; }
    }
}
