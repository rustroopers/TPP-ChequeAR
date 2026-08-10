using ChequeAR.Domain.Enums;

namespace ChequeAR.Application.Services
{
    /// <summary>
    /// Código de tipo de pregunta tal como lo hablan las islas Vue ("openChoice", no
    /// "openchoice" ni "open-choice"). Es el mismo vocabulario que devuelve
    /// FhirQuestionnaireReader al leer una versión publicada, así que el editor y el
    /// renderer comparan contra los mismos strings.
    ///
    /// Existe porque enum.ToString().ToLowerInvariant() no alcanza: aplana el camelCase y
    /// dejaba "openchoice"/"datetime", que no matcheaban ninguna opción del selector de
    /// tipo del editor —una pregunta de opción múltiple se reabría sin tipo seleccionado.
    /// </summary>
    public static class QuestionTypeMap
    {
        public static string ToUi(QuestionType type)
        {
            switch (type)
            {
                case QuestionType.OpenChoice: return "openChoice";
                case QuestionType.DateTime:   return "dateTime";
                default: return type.ToString().ToLowerInvariant();
            }
        }
    }
}
