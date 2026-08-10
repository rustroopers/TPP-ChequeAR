using System;
using System.Collections.Generic;

namespace ChequeAR.Api.Models
{
    public class QuestionnaireListItem
    {
        public Guid Id { get; set; }
        public string Title { get; set; }
        public string SpecialtyCode { get; set; }
        public string SpecialtyLabel { get; set; }
        public int Version { get; set; }
        public string Status { get; set; }
        public string UpdatedAt { get; set; }
        public string Author { get; set; }
        /// <summary>Publicado pero editado después: la versión viva no refleja los cambios.</summary>
        public bool HasUnpublishedChanges { get; set; }
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

        /// <summary>Opciones del filtro de especialidad, derivadas de los cuestionarios existentes.</summary>
        public List<SpecialtyOption> Specialties { get; set; } = new List<SpecialtyOption>();
    }

    public class SpecialtyOption
    {
        public string Code { get; set; }
        public string Label { get; set; }
    }

    public class QuestionnaireDetailViewModel
    {
        public Guid Id { get; set; }
        public string Title { get; set; }
        public string SpecialtyCode { get; set; }
        public string SpecialtyLabel { get; set; }
        public int Version { get; set; }
        public string Status { get; set; }
        public string Author { get; set; }
        public string UpdatedAt { get; set; }
        public bool HasUnpublishedChanges { get; set; }
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
        public decimal? Min { get; set; }
        public decimal? Max { get; set; }
        // Validación de texto (string/text): largo máximo y patrón (regex).
        public int? MaxLength { get; set; }
        public string Pattern { get; set; }
        public string Accept { get; set; }
        public EnableWhenView EnableWhen { get; set; }
        public string CodeSystem { get; set; }
        public string Code { get; set; }
        public string CodeDisplay { get; set; }
        // Concepto clínico que la pregunta mide, en palabras ("dolor torácico"), separado
        // del enunciado que lee el usuario ("¿Presentó dolor en el pecho al esfuerzo?").
        // Es la semilla de la búsqueda SNOMED: filtrar por la oración completa devuelve
        // poco y malo. Ver Term en la codificación de CodingJson.
        public string Term { get; set; }
        public List<string> Options { get; set; } = new List<string>();
    }

    /// <summary>Condición de visibilidad: "mostrar sólo si &lt;question&gt; &lt;op&gt; &lt;value&gt;".</summary>
    public class EnableWhenView
    {
        public string Question { get; set; }
        public string Op { get; set; }
        public object Value { get; set; }
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
