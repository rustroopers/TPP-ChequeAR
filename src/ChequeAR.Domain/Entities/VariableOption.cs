using System;

namespace ChequeAR.Domain.Entities
{
    /// <summary>
    /// Opción de respuesta de una variable de tipo Choice u OpenChoice. Es el espejo de
    /// AnswerOption más el coding, y tiene tabla propia a propósito: AnswerOption.QuestionId es
    /// NOT NULL y QuestionnaireRepository.ReplaceStructureAsync borra las opciones en bloque por
    /// pregunta, así que compartir tabla haría que guardar un cuestionario borrara el catálogo.
    /// Al insertar la variable, estas filas se COPIAN a AnswerOption.
    /// </summary>
    public class VariableOption
    {
        public Guid Id { get; set; }
        public Guid VariableId { get; set; }
        public string Value { get; set; }
        public string Display { get; set; }

        /// <summary>
        /// Código que viaja al FHIR y que las respuestas guardan en ValueCode. Cuando la opción
        /// está codificada en SNOMED, es igual a SnomedCode: R5 admite un solo Coding por
        /// answerOption.value, y unificarlos evita que el código del reporte difiera del del
        /// recurso. Se puede hacer justamente porque el catálogo arranca de cero.
        /// </summary>
        public string Code { get; set; }

        public string SnomedSystem { get; set; }
        public string SnomedCode { get; set; }
        public string SnomedDisplay { get; set; }
        public int OrderIndex { get; set; }
    }
}
