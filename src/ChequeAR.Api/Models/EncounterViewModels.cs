using System;
using System.Collections.Generic;

namespace ChequeAR.Api.Models
{
    public class PatientSearchItem
    {
        public string HC { get; set; }   // hospital id (string en el sistema del hospital)
        public string FullName { get; set; }
        public string AgeLabel { get; set; }
        /// <summary>DNI / documento del paciente (VW_PACIENTE.DOCUMENTO). Null si no viene.</summary>
        public string Document { get; set; }
        /// <summary>Fecha de nacimiento formateada dd/MM/yyyy. Null si no viene.</summary>
        public string BirthDateLabel { get; set; }
        public string SpecialtyCode { get; set; }
        public string SpecialtyLabel { get; set; }
    }

    public class PatientSearchViewModel
    {
        public string Query { get; set; }
        public List<PatientSearchItem> Results { get; set; } = new List<PatientSearchItem>();
        public bool HasSearched { get; set; }
        /// <summary>Cuestionarios publicados: son los únicos que se pueden cargar.</summary>
        public List<SelectableQuestionnaire> Questionnaires { get; set; } = new List<SelectableQuestionnaire>();
    }

    public class SelectableQuestionnaire
    {
        public Guid Id { get; set; }
        public string Title { get; set; }
        public int Version { get; set; }
        public string SpecialtyCode { get; set; }
        public string SpecialtyLabel { get; set; }
    }

    public class EncounterTimelineItem
    {
        /// <summary>Fecha real (UTC), usada para filtrar por rango; no se muestra tal cual.</summary>
        public DateTime RawDate { get; set; }
        public string Date { get; set; }
        public Guid QuestionnaireId { get; set; }
        public string QuestionnaireTitle { get; set; }
        public int QuestionnaireVersion { get; set; }
        public string SpecialtyCode { get; set; }
        public string SpecialtyLabel { get; set; }
        public string Author { get; set; }
        public Guid EncounterId { get; set; }
        public string Status { get; set; }
    }

    public class TimelineFilterOption
    {
        public string Value { get; set; }
        public string Label { get; set; }
    }

    public class PatientTimelineViewModel
    {
        public PatientSearchItem Patient { get; set; }
        public List<EncounterTimelineItem> Entries { get; set; } = new List<EncounterTimelineItem>();
        /// <summary>Si el paciente tiene fichas antes de aplicar filtros (distingue "sin historial" de "sin resultados para este filtro").</summary>
        public bool HasAnyEncounters { get; set; }

        // Filtros aplicados (para repoblar el form).
        public Guid? QuestionnaireFilter { get; set; }
        public string SpecialtyFilter { get; set; }
        public string AuthorFilter { get; set; }
        /// <summary>"pendiente" (InProgress), "finalizada" (Completed), o null = todas.</summary>
        public string StatusFilter { get; set; }
        public string From { get; set; }
        public string To { get; set; }

        // Opciones de filtro: sólo lo que efectivamente aparece en el historial de este paciente.
        public List<TimelineFilterOption> AvailableQuestionnaires { get; set; } = new List<TimelineFilterOption>();
        public List<TimelineFilterOption> AvailableSpecialties { get; set; } = new List<TimelineFilterOption>();
        public List<string> AvailableAuthors { get; set; } = new List<string>();
    }

    public class EncounterReadonlyViewModel
    {
        public Guid EncounterId { get; set; }
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
