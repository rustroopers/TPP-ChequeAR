using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using ChequeAR.Application.Fhir;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Application.Validation
{
    /// <summary>
    /// Valida las respuestas contra la versión anclada. El renderer aplica las mismas reglas,
    /// pero una llamada directa a la API no pasa por él.
    /// </summary>
    public static class ResponseValidator
    {
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

        // Grupos "(?" que JavaScript no tiene: modificadores en línea, atómicos, comentarios, condicionales.
        private static readonly Regex DotNetOnly = new Regex(@"(?<!\\)\(\?(?![:=!]|<[=!]|<[A-Za-z])", RegexOptions.None, RegexTimeout);

        /// <summary>
        /// La escala y los decimales se guardan en ValueDecimal, que es donde los buscan los
        /// reportes; un cliente FHIR manda la escala como integer, que es su tipo en el Questionnaire.
        /// </summary>
        public static void NormalizeColumns(RenderableQuestionnaire structure, IEnumerable<QuestionnaireResponseItem> items)
        {
            if (structure == null || items == null) return;
            var decimals = new HashSet<string>(structure.Groups.SelectMany(g => g.Questions)
                .Where(q => q.LinkId != null && (q.Type == "scale" || q.Type == "decimal")).Select(q => q.LinkId));
            foreach (var item in items)
            {
                if (item?.ValueInteger == null || item.ValueDecimal.HasValue || !decimals.Contains(item.LinkId ?? string.Empty)) continue;
                item.ValueDecimal = item.ValueInteger;
                item.ValueInteger = null;
            }
        }

        /// <param name="complete">
        /// False en el guardado parcial: sólo lo que nunca puede ser válido (pregunta inexistente,
        /// tipo equivocado, opción que no existe). Rango, largo y formato se exigen al finalizar,
        /// porque el autosave guarda lo que se está tipeando.
        /// </param>
        public static List<string> Validate(
            RenderableQuestionnaire structure, IEnumerable<QuestionnaireResponseItem> items, bool complete)
        {
            var errors = new List<string>();
            if (structure == null) return errors;

            var questions = new Dictionary<string, RenderableQuestion>();
            foreach (var q in structure.Groups.SelectMany(g => g.Questions))
                if (q.LinkId != null && !questions.ContainsKey(q.LinkId)) questions[q.LinkId] = q;

            foreach (var group in (items ?? Enumerable.Empty<QuestionnaireResponseItem>())
                         .Where(i => i != null).GroupBy(i => i.LinkId ?? string.Empty))
            {
                if (!questions.TryGetValue(group.Key, out var q))
                {
                    if (group.Any(HasValue)) errors.Add("La pregunta '" + group.Key + "' no existe en la versión del cuestionario.");
                    continue;
                }
                var answered = group.Where(HasValue).ToList();
                if (answered.Count > 1 && q.Type != "openChoice")
                    errors.Add(Label(q) + " admite una sola respuesta.");
                foreach (var item in answered)
                {
                    var error = Check(q, item, complete);
                    if (error != null) errors.Add(Label(q) + " " + error);
                }
            }
            return errors;
        }

        private static string Check(RenderableQuestion q, QuestionnaireResponseItem item, bool complete)
        {
            switch (q.Type)
            {
                case "boolean":
                    return Only(item, nameof(item.ValueBoolean)) ? null : WrongType;

                case "integer":
                    if (!Only(item, nameof(item.ValueInteger))) return WrongType;
                    return complete ? Range(q, item.ValueInteger.Value) : null;

                case "decimal":
                case "scale":
                    if (!Only(item, nameof(item.ValueDecimal))) return WrongType;
                    var d = item.ValueDecimal.Value;
                    if (q.Type == "scale" && d != decimal.Truncate(d)) return "tiene que ser un número entero.";
                    return complete ? Range(q, d) : null;

                case "date":
                case "dateTime":
                    return Only(item, nameof(item.ValueDate)) ? null : WrongType;

                case "choice":
                case "openChoice":
                    if (!Only(item, nameof(item.ValueCode))) return WrongType;
                    return q.Options.Any(o => o.Code == item.ValueCode) ? null : "tiene una opción que no existe.";

                case "attachment":
                    // El nombre del archivo viaja en ValueString junto al adjunto.
                    return item.AttachmentId.HasValue && OnlyAttachment(item) ? null : WrongType;

                default:
                    if (!Only(item, nameof(item.ValueString))) return WrongType;
                    return complete ? Text(q, item.ValueString) : null;
            }
        }

        private const string WrongType = "tiene un valor que no corresponde al tipo de la pregunta.";

        private static string Range(RenderableQuestion q, decimal value)
        {
            if (q.Min.HasValue && value < q.Min.Value) return "tiene que ser mayor o igual a " + Format(q.Min.Value) + ".";
            if (q.Max.HasValue && value > q.Max.Value) return "tiene que ser menor o igual a " + Format(q.Max.Value) + ".";
            return null;
        }

        private static string Text(RenderableQuestion q, string value)
        {
            if (q.MaxLength.HasValue && value.Length > q.MaxLength.Value)
                return "supera los " + q.MaxLength.Value + " caracteres.";
            if (string.IsNullOrEmpty(q.Pattern) || DotNetOnly.IsMatch(q.Pattern)) return null;
            // Con la sintaxis de JavaScript, que es la que evalúa el renderer. Un patrón que ahí no
            // compila es un error de quien armó el cuestionario y no bloquea, igual que en el navegador.
            try
            {
                return Regex.IsMatch(value, q.Pattern, RegexOptions.ECMAScript, RegexTimeout) ? null : "no tiene el formato esperado.";
            }
            catch (ArgumentException) { return null; }
            catch (RegexMatchTimeoutException) { return null; }
        }

        private static string Format(decimal v) => v.ToString("0.##", CultureInfo.GetCultureInfo("es-AR"));

        private static string Label(RenderableQuestion q) =>
            "\"" + (string.IsNullOrWhiteSpace(q.Text) ? q.LinkId : q.Text) + "\"";

        private static bool HasValue(QuestionnaireResponseItem i) =>
            i.ValueBoolean.HasValue || i.ValueInteger.HasValue || i.ValueDecimal.HasValue || i.ValueDate.HasValue
            || !string.IsNullOrEmpty(i.ValueCode) || !string.IsNullOrEmpty(i.ValueString) || i.AttachmentId.HasValue;

        /// <summary>Tiene valor sólo en la columna indicada.</summary>
        private static bool Only(QuestionnaireResponseItem i, string column) =>
            i.ValueBoolean.HasValue == (column == nameof(i.ValueBoolean))
            && i.ValueInteger.HasValue == (column == nameof(i.ValueInteger))
            && i.ValueDecimal.HasValue == (column == nameof(i.ValueDecimal))
            && i.ValueDate.HasValue == (column == nameof(i.ValueDate))
            && !string.IsNullOrEmpty(i.ValueCode) == (column == nameof(i.ValueCode))
            && !string.IsNullOrEmpty(i.ValueString) == (column == nameof(i.ValueString))
            && !i.AttachmentId.HasValue;

        private static bool OnlyAttachment(QuestionnaireResponseItem i) =>
            !i.ValueBoolean.HasValue && !i.ValueInteger.HasValue && !i.ValueDecimal.HasValue
            && !i.ValueDate.HasValue && string.IsNullOrEmpty(i.ValueCode);
    }
}
