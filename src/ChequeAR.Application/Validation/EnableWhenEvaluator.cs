using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ChequeAR.Application.Fhir;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Application.Validation
{
    /// <summary>
    /// Visibilidad (enableWhen) y completitud de una ficha, del lado del servidor.
    ///
    /// Replica a propósito la semántica del renderer (isVisible/compareAnswer/isEmptyAnswer
    /// en Scripts/islands/questionnaire-renderer): la validación del navegador es la que ve
    /// el médico, y el servidor no puede rechazar una ficha que la pantalla mostró completa.
    /// Lo que agrega es que la regla se cumpla aunque el cliente no la aplique (una llamada
    /// directa a $finalize).
    /// </summary>
    public static class EnableWhenEvaluator
    {
        /// <summary>
        /// Preguntas obligatorias, visibles y sin responder, en el orden del cuestionario.
        /// Lista vacía si la ficha está completa o si no hay estructura contra la cual validar.
        /// </summary>
        public static IReadOnlyList<RenderableQuestion> MissingRequired(
            RenderableQuestionnaire structure, IEnumerable<QuestionnaireResponseItem> items)
        {
            var missing = new List<RenderableQuestion>();
            if (structure == null) return missing;

            var answers = AnswersByLinkId(items);
            var questions = structure.Groups.SelectMany(g => g.Questions).ToList();
            var typeByLinkId = new Dictionary<string, string>();
            foreach (var q in questions)
                if (q.LinkId != null && !typeByLinkId.ContainsKey(q.LinkId)) typeByLinkId[q.LinkId] = q.Type;

            foreach (var q in questions)
            {
                if (!q.Required) continue;
                if (!IsVisible(q, answers, typeByLinkId)) continue;
                if (!answers.TryGetValue(q.LinkId ?? string.Empty, out var values) || values.Count == 0)
                    missing.Add(q);
            }
            return missing;
        }

        /// <summary>
        /// Vacía las respuestas de las preguntas que la condición enableWhen deja ocultas. FHIR
        /// lo exige (un ítem deshabilitado no puede tener respuestas) y es lo que ve el médico:
        /// una respuesta cargada cuando la pregunta estaba visible no puede sobrevivir a que la
        /// condición deje de cumplirse. La visibilidad se calcula una sola vez, con las
        /// respuestas originales, igual que el renderer.
        /// </summary>
        /// <returns>Los linkId cuyas respuestas se vaciaron.</returns>
        public static IReadOnlyList<string> ClearHiddenAnswers(
            RenderableQuestionnaire structure, ICollection<QuestionnaireResponseItem> items)
        {
            var cleared = new List<string>();
            if (structure == null || items == null || items.Count == 0) return cleared;

            var answers = AnswersByLinkId(items);
            var questions = structure.Groups.SelectMany(g => g.Questions).ToList();
            var typeByLinkId = new Dictionary<string, string>();
            foreach (var q in questions)
                if (q.LinkId != null && !typeByLinkId.ContainsKey(q.LinkId)) typeByLinkId[q.LinkId] = q.Type;

            var hidden = new HashSet<string>(questions
                .Where(q => q.LinkId != null && !IsVisible(q, answers, typeByLinkId))
                .Select(q => q.LinkId));

            foreach (var item in items)
            {
                if (item?.LinkId == null || !hidden.Contains(item.LinkId) || ValueOf(item) == null) continue;
                item.ValueBoolean = null;
                item.ValueInteger = null;
                item.ValueDecimal = null;
                item.ValueDate = null;
                item.ValueString = null;
                item.ValueCode = null;
                item.AttachmentId = null;
                if (!cleared.Contains(item.LinkId)) cleared.Add(item.LinkId);
            }
            return cleared;
        }

        /// <summary>linkId -> valores respondidos (sin los vacíos). Una opción múltiple aporta un valor por opción.</summary>
        public static Dictionary<string, List<object>> AnswersByLinkId(IEnumerable<QuestionnaireResponseItem> items)
        {
            var result = new Dictionary<string, List<object>>();
            foreach (var item in items ?? Enumerable.Empty<QuestionnaireResponseItem>())
            {
                var value = ValueOf(item);
                if (value == null) continue;
                var key = item.LinkId ?? string.Empty;
                if (!result.TryGetValue(key, out var list)) result[key] = list = new List<object>();
                list.Add(value);
            }
            return result;
        }

        /// <param name="typeByLinkId">Tipo de cada pregunta: decide si la origen es de respuesta múltiple.</param>
        public static bool IsVisible(
            RenderableQuestion q,
            IReadOnlyDictionary<string, List<object>> answers,
            IReadOnlyDictionary<string, string> typeByLinkId)
        {
            var cond = q?.EnableWhen;
            if (cond == null) return true;

            answers.TryGetValue(cond.Question ?? string.Empty, out var values);
            values = values ?? new List<object>();

            // FHIR R5 y el renderer: con respuesta múltiple, alcanza con que UNA opción cumpla.
            // Un múltiple sin nada marcado no cumple ninguna condición (ni siquiera '!='),
            // porque en el navegador es un array vacío y no "sin responder".
            var multiple = values.Count > 1
                           || (typeByLinkId != null && typeByLinkId.TryGetValue(cond.Question ?? string.Empty, out var t)
                               && t == "openChoice");
            if (multiple) return values.Any(v => Compare(cond.Op, v, cond.Value));

            return Compare(cond.Op, values.Count == 0 ? null : values[0], cond.Value);
        }

        /// <summary>compareAnswer del renderer. Un operador desconocido no oculta la pregunta.</summary>
        public static bool Compare(string op, object actual, object expected)
        {
            switch (op)
            {
                case "=": return SameAnswer(actual, expected);
                case "!=": return !SameAnswer(actual, expected);
                // Sin respuesta, una comparación numérica no se cumple (no se compara contra NaN).
                case ">": return actual != null && Numeric(actual) > Numeric(expected);
                case "<": return actual != null && Numeric(actual) < Numeric(expected);
                default: return true;
            }
        }

        private static bool SameAnswer(object actual, object expected)
        {
            if (actual == null) return false;
            if (IsNumber(expected))
            {
                var n = Numeric(actual);
                return n.HasValue && n == Numeric(expected);
            }
            if (expected is bool b) return actual is bool a && a == b;
            if (expected is string s) return actual is string x && string.Equals(x, s, StringComparison.Ordinal);
            return Equals(actual, expected);
        }

        private static bool IsNumber(object v) => v is long || v is int || v is decimal || v is double;

        /// <summary>parseNumeric del renderer: acepta coma decimal (es-AR). Null si no es número.</summary>
        private static decimal? Numeric(object v)
        {
            switch (v)
            {
                case null: return null;
                case long l: return l;
                case int i: return i;
                case decimal d: return d;
                case double db: return (decimal)db;
                case string s:
                    var raw = s.Trim().Replace(',', '.');
                    return decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                        ? parsed : (decimal?)null;
                default: return null;
            }
        }

        /// <summary>El valor respondido de un item, o null si está vacío (isEmptyAnswer del renderer).</summary>
        private static object ValueOf(QuestionnaireResponseItem item)
        {
            if (item == null) return null;
            if (item.ValueBoolean.HasValue) return item.ValueBoolean.Value;
            if (item.ValueInteger.HasValue) return (long)item.ValueInteger.Value;
            if (item.ValueDecimal.HasValue) return item.ValueDecimal.Value;
            if (item.ValueDate.HasValue) return item.ValueDate.Value;
            if (!string.IsNullOrEmpty(item.ValueCode)) return item.ValueCode;
            if (!string.IsNullOrEmpty(item.ValueString)) return item.ValueString;
            if (item.AttachmentId.HasValue) return item.AttachmentId.Value;
            return null;
        }
    }
}
