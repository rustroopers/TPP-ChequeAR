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

        /// <summary>
        /// Inverso de <see cref="ToUi"/>, para el ABM de variables: el formulario manda el
        /// mismo vocabulario que consume el editor, y el catálogo lo tiene que guardar como
        /// enum. Case-insensitive porque "openChoice" y "openchoice" son el mismo tipo y no
        /// vale la pena rechazar un request por una mayúscula.
        ///
        /// Un valor desconocido cae a String en vez de tirar: así un cliente desactualizado
        /// provoca un 422 con el detalle de la validación y no un 500 opaco.
        /// </summary>
        public static QuestionType FromUi(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return QuestionType.String;
            foreach (QuestionType t in System.Enum.GetValues(typeof(QuestionType)))
                if (string.Equals(ToUi(t), code, System.StringComparison.OrdinalIgnoreCase))
                    return t;
            return QuestionType.String;
        }
    }
}
