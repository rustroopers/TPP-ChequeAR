using System;
using ChequeAR.Application.Exceptions;
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

        /// <summary>Etiqueta legible del tipo ("Opción única"), la del catálogo de variables.</summary>
        public static string Label(QuestionType type)
        {
            switch (type)
            {
                case QuestionType.String: return "Texto corto";
                case QuestionType.Text: return "Texto largo";
                case QuestionType.Integer: return "Número entero";
                case QuestionType.Decimal: return "Número decimal";
                case QuestionType.Boolean: return "Sí / No";
                case QuestionType.Choice: return "Opción única";
                case QuestionType.OpenChoice: return "Opción múltiple";
                case QuestionType.Date: return "Fecha";
                case QuestionType.DateTime: return "Fecha y hora";
                case QuestionType.Attachment: return "Adjunto";
                case QuestionType.Scale: return "Escala";
                default: return type.ToString();
            }
        }

        /// <summary>
        /// Inverso de <see cref="ToUi"/>, para el ABM de variables: el formulario manda el
        /// mismo vocabulario que consume el editor, y el catálogo lo tiene que guardar como
        /// enum. Case-insensitive porque "openChoice" y "openchoice" son el mismo tipo y no
        /// vale la pena rechazar un request por una mayúscula.
        ///
        /// Sin tipo se asume String (el formulario lo trae preseleccionado). Un tipo que no
        /// existe es un error del cliente y se rechaza con 422: convertirlo en String en
        /// silencio crearía una variable distinta de la que se pidió.
        /// </summary>
        /// <exception cref="ValidationFailedException">El código no corresponde a ningún tipo.</exception>
        public static QuestionType FromUi(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return QuestionType.String;
            if (TryFromUi(code, out var type)) return type;
            throw new ValidationFailedException(new[] { "Tipo de pregunta desconocido: '" + code + "'." });
        }

        /// <summary>Como <see cref="FromUi"/>, sin excepción: false si el código no corresponde a ningún tipo.</summary>
        public static bool TryFromUi(string code, out QuestionType type)
        {
            foreach (QuestionType t in Enum.GetValues(typeof(QuestionType)))
            {
                if (string.Equals(ToUi(t), code?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    type = t;
                    return true;
                }
            }
            type = QuestionType.String;
            return false;
        }
    }
}
