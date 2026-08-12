using System;
using System.Collections.Generic;

namespace ChequeAR.Api.Models
{
    public class PatientSearchItem
    {
        public string HC { get; set; }   // hospital id (string en el sistema del hospital)
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

    public class PatientTrendViewModel
    {
        public PatientSearchItem Patient { get; set; }
        /// <summary>El cuestionario elegido en el selector (null si todavía no eligió ninguno).</summary>
        public Guid? SelectedQuestionnaireId { get; set; }
        /// <summary>Cuestionarios que el paciente completó al menos una vez: los únicos con datos para graficar.</summary>
        public List<TimelineFilterOption> AvailableQuestionnaires { get; set; } = new List<TimelineFilterOption>();
        /// <summary>Si el paciente tiene alguna ficha finalizada (distingue "sin historial" de "no eligió cuestionario").</summary>
        public bool HasAnyCompleted { get; set; }
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

    /// <summary>
    /// Historial de UNA ficha: quién y cuándo tocó qué cosa, reconstruido de los eventos de
    /// auditoría de esa respuesta (<c>QuestionnaireResponse/{id}$save|$finalize|$lock…</c>).
    /// Es la vista por-ficha de lo que Auditoría muestra a nivel global.
    /// </summary>
    public class EncounterHistoryViewModel
    {
        public Guid EncounterId { get; set; }
        public PatientSearchItem Patient { get; set; }
        public Guid QuestionnaireId { get; set; }
        public string QuestionnaireTitle { get; set; }
        public int QuestionnaireVersion { get; set; }
        /// <summary>"completed" / "inprogress": decide si se ofrece "Ver ficha" (solo finalizadas) y el texto de "volver a completar".</summary>
        public string Status { get; set; }
        public List<EncounterHistoryEntry> Entries { get; set; } = new List<EncounterHistoryEntry>();
    }

    public class EncounterHistoryEntry
    {
        public string Timestamp { get; set; }
        public string Username { get; set; }
        public string Label { get; set; }
        public string BadgeClass { get; set; }
        public string Icon { get; set; }
        /// <summary>Diff de la sesión, sólo presente en los "Avance guardado" ($save).</summary>
        public List<ChequeAR.Application.Services.ResponseFieldChange> Changes { get; set; } = new List<ChequeAR.Application.Services.ResponseFieldChange>();
    }

    public class AnsweredQuestion
    {
        public string Text { get; set; }
        public string Answer { get; set; }
        public string Unit { get; set; }
        public bool Prefilled { get; set; }
    }
}
