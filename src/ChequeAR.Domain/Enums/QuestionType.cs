namespace ChequeAR.Domain.Enums
{
    public enum QuestionType
    {
        Decimal,
        Integer,
        String,
        Text,
        Date,
        DateTime,
        Boolean,
        /// <summary>Una sola opción del catálogo (FHIR: coding + answerConstraint optionsOnly).</summary>
        Choice,

        /// <summary>
        /// Selección MÚLTIPLE entre las opciones del catálogo, sin texto libre (FHIR: coding +
        /// optionsOnly + repeats). El nombre viene del vocabulario R4 y se conserva porque es
        /// el que hablan la base y las islas; no significa "opción abierta".
        /// </summary>
        OpenChoice,
        Attachment,
        Scale
    }
}
