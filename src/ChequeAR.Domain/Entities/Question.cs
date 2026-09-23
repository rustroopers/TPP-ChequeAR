using System;
using System.Collections.Generic;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Domain.Entities
{
    public class Question
    {
        public Guid Id { get; set; }
        public Guid QuestionnaireId { get; set; }
        public Guid? GroupId { get; set; }
        public string LinkId { get; set; }
        public string Text { get; set; }
        public QuestionType Type { get; set; }
        public bool Required { get; set; }
        public bool RepeatsAllowed { get; set; }
        public int OrderIndex { get; set; }
        public string ValidationJson { get; set; }
        public string EnableWhenJson { get; set; }
        public string CodingJson { get; set; }

        /// <summary>
        /// Variable del catálogo de la que proviene esta pregunta. Nullable en la base porque un
        /// Questionnaire FHIR posteado por un sistema externo llega sin ella -y con NOT NULL eso
        /// sería un ORA-01400 (500) en vez del 422 que corresponde-. La regla dura "toda pregunta
        /// viene de una variable activa" la aplica QuestionnaireService, no el esquema.
        /// </summary>
        public Guid? VariableId { get; set; }

        /// <summary>
        /// Revisión de la variable al momento de insertarla. Lo que se guardó acá es un snapshot:
        /// si el Administrador edita la variable después, esta pregunta NO cambia. Comparar contra
        /// ClinicalVariable.Revision es lo que permite avisar en el borrador que quedó desfasada.
        /// </summary>
        public int VariableRevision { get; set; }

        public virtual ICollection<AnswerOption> Options { get; set; } = new List<AnswerOption>();
    }
}
