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
    /// Tiene que dar lo mismo que el renderer (Scripts/islands/questionnaire-renderer/enable-when.js):
    /// la validación del navegador es la que ve el médico, y si los dos lados difieren, $finalize
    /// borra respuestas que la pantalla mostraba o exige preguntas que no mostraba. Los tests de
    /// paridad corren los mismos casos contra los dos.
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

            var answers = AnswersByLinkId(items, structure);
            var hidden = HiddenLinkIds(structure, answers);
            foreach (var q in structure.Groups.SelectMany(g => g.Questions))
            {
                if (!q.Required || hidden.Contains(q.LinkId ?? string.Empty)) continue;
                if (!answers.TryGetValue(q.LinkId ?? string.Empty, out var values) || values.Count == 0)
                    missing.Add(q);
            }
            return missing;
        }

        /// <summary>
        /// Vacía las respuestas de las preguntas que la condición enableWhen deja ocultas. FHIR
        /// lo exige (un ítem deshabilitado no puede tener respuestas) y es lo que ve el médico:
        /// una respuesta cargada cuando la pregunta estaba visible no puede sobrevivir a que la
        /// condición deje de cumplirse.
        /// </summary>
        /// <returns>Los linkId cuyas respuestas se vaciaron.</returns>
        public static IReadOnlyList<string> ClearHiddenAnswers(
            RenderableQuestionnaire structure, ICollection<QuestionnaireResponseItem> items)
        {
            var cleared = new List<string>();
            if (structure == null || items == null || items.Count == 0) return cleared;

            var hidden = HiddenLinkIds(structure, AnswersByLinkId(items, structure));
            foreach (var item in items)
            {
                if (item?.LinkId == null || !hidden.Contains(item.LinkId) || ValueOf(item, null) == null) continue;
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

        /// <summary>
        /// linkId -> valores respondidos (sin los vacíos). Una opción múltiple aporta un valor por
        /// opción. Las fechas se pasan a texto según el tipo de la pregunta, que es como las
        /// compara el renderer.
        /// </summary>
        public static Dictionary<string, List<object>> AnswersByLinkId(
            IEnumerable<QuestionnaireResponseItem> items, RenderableQuestionnaire structure = null)
        {
            var types = TypesByLinkId(structure);
            var result = new Dictionary<string, List<object>>();
            foreach (var item in items ?? Enumerable.Empty<QuestionnaireResponseItem>())
            {
                var key = item?.LinkId ?? string.Empty;
                types.TryGetValue(key, out var type);
                var value = ValueOf(item, type);
                if (value == null) continue;
                if (!result.TryGetValue(key, out var list)) result[key] = list = new List<object>();
                list.Add(value);
            }
            return result;
        }

        /// <summary>
        /// Preguntas ocultas. Una condición cuya pregunta de origen está oculta se evalúa como si
        /// la origen no tuviera respuesta, aunque tenga un valor guardado.
        /// </summary>
        public static HashSet<string> HiddenLinkIds(
            RenderableQuestionnaire structure, IReadOnlyDictionary<string, List<object>> answers)
        {
            var byLinkId = new Dictionary<string, RenderableQuestion>();
            foreach (var q in structure?.Groups.SelectMany(g => g.Questions) ?? Enumerable.Empty<RenderableQuestion>())
                if (q.LinkId != null && !byLinkId.ContainsKey(q.LinkId)) byLinkId[q.LinkId] = q;

            var hidden = new HashSet<string>();
            foreach (var q in byLinkId.Values)
                if (!IsVisible(q, byLinkId, answers, new HashSet<string>())) hidden.Add(q.LinkId);
            return hidden;
        }

        private static bool IsVisible(
            RenderableQuestion q,
            IReadOnlyDictionary<string, RenderableQuestion> byLinkId,
            IReadOnlyDictionary<string, List<object>> answers,
            HashSet<string> visiting)
        {
            var cond = q?.EnableWhen;
            if (cond == null) return true;
            // Un ciclo lo rechaza QuestionnaireStructureRules; si llega igual, no oculta nada.
            if (!visiting.Add(q.LinkId ?? string.Empty)) return true;

            byLinkId.TryGetValue(cond.Question ?? string.Empty, out var source);
            var sourceVisible = source == null || IsVisible(source, byLinkId, answers, visiting);
            visiting.Remove(q.LinkId ?? string.Empty);

            List<object> values = null;
            if (sourceVisible) answers.TryGetValue(cond.Question ?? string.Empty, out values);
            return Satisfies(cond.Op, values ?? new List<object>(), Normalize(source?.Type, cond.Value));
        }

        /// <summary>Una sola respuesta (o ninguna, si es null) contra el valor esperado.</summary>
        public static bool Compare(string op, object actual, object expected)
            => Satisfies(op, actual == null ? new List<object>() : new List<object> { actual }, expected);

        /// <summary>
        /// FHIR R5: '=' y los de orden se cumplen si alguna respuesta cumple; '!=' si ninguna es
        /// igual. Sin respuesta, '!=' se cumple y el resto no. Un operador desconocido no oculta.
        /// </summary>
        public static bool Satisfies(string op, IList<object> values, object expected)
        {
            switch (op)
            {
                case "=": return values.Any(v => SameAnswer(v, expected));
                case "!=": return !values.Any(v => SameAnswer(v, expected));
                case ">": return values.Any(v => CompareNumeric(v, expected) > 0);
                case "<": return values.Any(v => CompareNumeric(v, expected) < 0);
                default: return true;
            }
        }

        private static bool SameAnswer(object actual, object expected)
        {
            if (actual == null || expected == null) return false;
            if (IsNumber(expected))
            {
                var n = Numeric(actual);
                return n.HasValue && n == Numeric(expected);
            }
            if (expected is bool b) return actual is bool a && a == b;
            if (expected is string s) return actual is string x && string.Equals(x, s, StringComparison.Ordinal);
            return Equals(actual, expected);
        }

        /// <summary>Signo de actual - expected, o 0 si alguno no es número (y la comparación no se cumple).</summary>
        private static int CompareNumeric(object actual, object expected)
        {
            var n = Numeric(actual);
            var e = Numeric(expected);
            if (!n.HasValue || !e.HasValue) return 0;
            return n.Value.CompareTo(e.Value);
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
                    var raw = s.Trim();
                    var coma = raw.IndexOf(',');
                    if (coma >= 0) raw = raw.Substring(0, coma) + "." + raw.Substring(coma + 1);
                    return decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                        ? parsed : (decimal?)null;
                default: return null;
            }
        }

        private static object Normalize(string type, object value)
        {
            if (!(value is string s)) return value;
            if (type == "date" && s.Length > 10) return s.Substring(0, 10);
            if (type == "dateTime" && s.Length > 16) return s.Substring(0, 16);
            return s;
        }

        private static Dictionary<string, string> TypesByLinkId(RenderableQuestionnaire structure)
        {
            var types = new Dictionary<string, string>();
            foreach (var q in structure?.Groups.SelectMany(g => g.Questions) ?? Enumerable.Empty<RenderableQuestion>())
                if (q.LinkId != null && !types.ContainsKey(q.LinkId)) types[q.LinkId] = q.Type;
            return types;
        }

        /// <summary>El valor respondido de un item, o null si está vacío.</summary>
        private static object ValueOf(QuestionnaireResponseItem item, string type)
        {
            if (item == null) return null;
            if (item.ValueBoolean.HasValue) return item.ValueBoolean.Value;
            if (item.ValueInteger.HasValue) return (long)item.ValueInteger.Value;
            if (item.ValueDecimal.HasValue) return item.ValueDecimal.Value;
            if (item.ValueDate.HasValue)
                return item.ValueDate.Value.ToString(
                    type == "dateTime" ? "yyyy-MM-dd'T'HH:mm" : "yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(item.ValueCode)) return item.ValueCode;
            if (!string.IsNullOrEmpty(item.ValueString)) return item.ValueString;
            if (item.AttachmentId.HasValue) return item.AttachmentId.Value;
            return null;
        }
    }
}
