using System;

namespace ChequeAR.Domain.Entities
{
    public class AnswerOption
    {
        public Guid Id { get; set; }
        public Guid QuestionId { get; set; }
        public string Value { get; set; }
        public string Display { get; set; }
        public string Code { get; set; }

        /// <summary>
        /// Coding copiado desde VariableOption al insertar la variable. Las columnas ya existían
        /// en Oracle desde la migración 202607291803214_Q_AnswerOptionSnomedCoding, que las creó
        /// sin entidad ni uso; recién ahora, con el catálogo, tienen dueño. No se guarda el
        /// system: si hay SnomedCode, es SNOMED -el builder FHIR lo completa con la constante-.
        /// </summary>
        public string SnomedCode { get; set; }
        public string SnomedDisplay { get; set; }

        public int OrderIndex { get; set; }
    }
}
