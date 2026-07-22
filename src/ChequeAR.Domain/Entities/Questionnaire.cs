using System;
using System.Collections.Generic;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Domain.Entities
{
    public class Questionnaire
    {
        public Guid Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public QuestionnaireStatus Status { get; set; }
        public string SpecialtyCode { get; set; }
        public string SpecialtyDisplay { get; set; }
        public Guid? CurrentVersionId { get; set; }
        public string CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool IsDeleted { get; set; }

        /// <summary>
        /// Se editó después de publicar y todavía no se publicó una versión con esos
        /// cambios. Es un flag explícito y no una comparación UpdatedAt &gt; PublishedAt
        /// porque en Oracle esas columnas son DATE (precisión de 1 segundo): una edición
        /// dentro del mismo segundo que la publicación quedaba invisible.
        /// </summary>
        public bool HasPendingChanges { get; set; }

        public virtual ICollection<QuestionnaireVersion> Versions { get; set; } = new List<QuestionnaireVersion>();
        public virtual ICollection<QuestionGroup> Groups { get; set; } = new List<QuestionGroup>();
        public virtual ICollection<Question> Questions { get; set; } = new List<Question>();

        /// <summary>
        /// Publicable si no está archivado y tiene al menos una pregunta. Un cuestionario
        /// ya activo se puede volver a publicar: eso congela una versión nueva (v2, v3…)
        /// con los cambios hechos desde la última publicación.
        /// </summary>
        public bool CanPublish()
        {
            return Status != QuestionnaireStatus.Retired && Questions != null && Questions.Count > 0;
        }

        /// <summary>
        /// Un cuestionario publicado que se siguió editando: la versión congelada sigue
        /// sirviendo a los clínicos (las respuestas apuntan a QuestionnaireVersionId) y los
        /// cambios quedan pendientes hasta la próxima publicación. En un borrador el flag
        /// no significa nada: todavía no hay versión publicada con la que contrastar.
        /// </summary>
        public bool HasUnpublishedChanges()
        {
            return Status == QuestionnaireStatus.Active && HasPendingChanges;
        }
    }
}
