using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Application.Reports
{
    /// <summary>
    /// Metadata del cuestionario que un reporte necesita para armar SQL correcto: de qué
    /// tipo es cada pregunta y, por lo tanto, en qué columna de QuestionnaireResponseItem
    /// cayó su respuesta.
    ///
    /// Antes el QueryBuilder deducía la columna del tipo CLR del VALOR que mandaba el
    /// cliente. Eso fallaba en silencio —sin error, con cero filas— cada vez que el tipo
    /// del valor no coincidía con el de la pregunta. El caso más visible: una pregunta de
    /// opción única guarda la respuesta en ValueCode, pero el filtro llegaba como string y
    /// consultaba ValueString, que para esas respuestas siempre es NULL.
    ///
    /// Es también el punto donde se valida que un linkId pertenezca al cuestionario: un
    /// linkId inventado ahora es un error explícito y no una cohorte vacía.
    /// </summary>
    public class ReportSchema
    {
        private readonly Dictionary<string, QuestionType> _types;

        /// <summary>Pares (linkId, texto) en orden de pregunta, para pivotear a formato ancho.</summary>
        public IList<KeyValuePair<string, string>> Columns { get; }

        private ReportSchema(Dictionary<string, QuestionType> types, IList<KeyValuePair<string, string>> columns)
        {
            _types = types;
            Columns = columns;
        }

        public static ReportSchema From(Questionnaire q)
        {
            var types = new Dictionary<string, QuestionType>(StringComparer.OrdinalIgnoreCase);
            var columns = new List<KeyValuePair<string, string>>();
            foreach (var question in (q?.Questions ?? new List<Question>()).OrderBy(x => x.OrderIndex))
            {
                if (string.IsNullOrWhiteSpace(question.LinkId)) continue;
                types[question.LinkId] = question.Type;
                columns.Add(new KeyValuePair<string, string>(question.LinkId, question.Text));
            }
            return new ReportSchema(types, columns);
        }

        public bool Knows(string linkId) =>
            !string.IsNullOrWhiteSpace(linkId) && _types.ContainsKey(linkId);

        public QuestionType TypeOf(string linkId)
        {
            if (!Knows(linkId))
                throw new ArgumentException("La pregunta '" + linkId + "' no pertenece a este cuestionario.");
            return _types[linkId];
        }

        /// <summary>Columna tipada de QuestionnaireResponseItem donde vive la respuesta.</summary>
        public string ColumnFor(string linkId)
        {
            switch (TypeOf(linkId))
            {
                case QuestionType.Decimal:
                case QuestionType.Scale:
                    return "ValueDecimal";
                // Los enteros se guardan en su propia columna, no en ValueDecimal.
                case QuestionType.Integer:
                    return "ValueInteger";
                case QuestionType.Date:
                case QuestionType.DateTime:
                    return "ValueDate";
                case QuestionType.Boolean:
                    return "ValueBoolean";
                // Opción única y múltiple guardan el código de la opción elegida.
                case QuestionType.Choice:
                case QuestionType.OpenChoice:
                    return "ValueCode";
                case QuestionType.Attachment:
                    throw new ArgumentException("No se puede filtrar por una pregunta de tipo adjunto.");
                default:
                    return "ValueString";
            }
        }

        /// <summary>
        /// Lleva el valor que llegó por JSON al tipo que espera la columna. Newtonsoft
        /// entrega los enteros como long y los reales como double, y todo lo que venga de un
        /// input de texto llega como string aunque la pregunta sea numérica.
        /// </summary>
        public object Coerce(string linkId, object raw)
        {
            if (raw == null) return null;
            var text = raw as string;

            switch (TypeOf(linkId))
            {
                case QuestionType.Decimal:
                case QuestionType.Scale:
                case QuestionType.Integer:
                    if (text != null)
                    {
                        // Coma decimal: es lo que tipea un usuario es-AR.
                        var normalizado = text.Trim().Replace(',', '.');
                        if (!decimal.TryParse(normalizado, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                            throw new ArgumentException("'" + text + "' no es un número válido para esta pregunta.");
                        return d;
                    }
                    return Convert.ToDecimal(raw, CultureInfo.InvariantCulture);

                case QuestionType.Boolean:
                    // Oracle no tiene BOOLEAN en tablas: ValueBoolean es NUMBER(1).
                    var b = raw is bool flag
                        ? flag
                        : string.Equals(Convert.ToString(raw), "true", StringComparison.OrdinalIgnoreCase);
                    return b ? 1 : 0;

                case QuestionType.Date:
                case QuestionType.DateTime:
                    if (raw is DateTime dt) return dt;
                    if (!DateTime.TryParse(Convert.ToString(raw), CultureInfo.InvariantCulture,
                                           DateTimeStyles.None, out var parsed))
                        throw new ArgumentException("'" + raw + "' no es una fecha válida.");
                    return parsed;

                default:
                    return Convert.ToString(raw);
            }
        }
    }
}
