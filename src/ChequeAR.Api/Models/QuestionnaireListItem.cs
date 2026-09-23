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
        public string SpecialtyColor { get; set; }
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
        public string SpecialtyColor { get; set; }
        public int Version { get; set; }
        public string Status { get; set; }
        public string Author { get; set; }
        public string UpdatedAt { get; set; }
        public bool HasUnpublishedChanges { get; set; }

        /// <summary>
        /// Versión puntual que se está mirando desde el historial. null = el cuestionario
        /// actual. Cuando tiene valor, las preguntas salen del FHIR congelado de esa versión.
        /// </summary>
        public int? SnapshotVersion { get; set; }

        public List<QuestionGroupView> Groups { get; set; } = new List<QuestionGroupView>();
    }

    /// <summary>Comparación entre dos versiones publicadas (pantalla Historial → Comparar).</summary>
    public class QuestionnaireCompareViewModel
    {
        public Guid Id { get; set; }
        public string Title { get; set; }
        public int From { get; set; }
        public int To { get; set; }
        public string FromDate { get; set; }
        public string ToDate { get; set; }
        public string FromAuthor { get; set; }
        public string ToAuthor { get; set; }
        public string ToChangeNote { get; set; }

        /// <summary>Todas las versiones publicadas, para los selectores de la pantalla.</summary>
        public List<int> Versions { get; set; } = new List<int>();

        public ChequeAR.Application.Services.QuestionnaireDiff Diff { get; set; }
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

        /// <summary>
        /// Variable del catálogo de la que proviene la pregunta. El editor la necesita para
        /// reenviarla al guardar y para mostrar con candado lo que la variable define.
        /// </summary>
        public string VariableId { get; set; }

        /// <summary>
        /// Nombre de la variable, sólo para mostrar. Se resuelve una vez por request contra el
        /// catálogo; sin esto el panel de propiedades necesitaría un fetch por pregunta para
        /// poder decir de dónde sale cada una.
        /// </summary>
        public string VariableName { get; set; }

        /// <summary>Revisión con la que se armó la pregunta (el snapshot congelado).</summary>
        public int VariableRevision { get; set; }

        /// <summary>
        /// Revisión vigente de esa variable hoy. Si es mayor que VariableRevision, el
        /// Administrador la editó después: el editor lo avisa, pero no re-sincroniza nada.
        /// </summary>
        public int VariableCurrentRevision { get; set; }
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
