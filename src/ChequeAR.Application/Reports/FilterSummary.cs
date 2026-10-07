using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ChequeAR.Application.Dtos;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Application.Reports
{
    /// <summary>
    /// Vuelca un FilterNode a lenguaje natural ("Peso mayor o igual a 10 y (Especialidad es
    /// Cardiología o Especialidad es Clínica médica)") para que un export "viva" solo -quien
    /// lo recibe por mail no tiene el constructor de consultas al lado para saber qué se
    /// filtró, y menos todavía para leer operadores sueltos como iría en un WHERE de SQL-.
    /// </summary>
    public static class FilterSummary
    {
        public static string Describe(FilterNode node, ReportSchema schema)
        {
            var texto = Node(node, schema);
            return string.IsNullOrEmpty(texto) ? "Sin filtros" : texto;
        }

        private static string Node(FilterNode node, ReportSchema schema)
        {
            if (node?.Predicates == null || node.Predicates.Count == 0) return "";
            var partes = node.Predicates.Select(p => Predicate(p, schema)).Where(s => !string.IsNullOrEmpty(s));
            var conector = string.Equals(node.Logic, "OR", StringComparison.OrdinalIgnoreCase) ? " o " : " y ";
            return string.Join(conector, partes);
        }

        private static string Predicate(FilterPredicate p, ReportSchema schema)
        {
            if (p.Group != null)
            {
                var interno = Node(p.Group, schema);
                return string.IsNullOrEmpty(interno) ? "" : "(" + interno + ")";
            }

            var texto = schema.Knows(p.LinkId)
                ? schema.Columns.FirstOrDefault(c => c.Key == p.LinkId).Value ?? p.LinkId
                : p.LinkId;
            var op = (p.Op ?? "").ToLowerInvariant();

            if (op == "between")
            {
                var valores = AsList(p.Value);
                return valores.Count == 2
                    ? texto + " entre " + FormatValue(valores[0], p.LinkId, schema) + " y " + FormatValue(valores[1], p.LinkId, schema)
                    : texto + " entre " + FormatValue(p.Value, p.LinkId, schema);
            }
            if (op == "exists")
            {
                var tieneValor = p.Value == null || !string.Equals(Convert.ToString(p.Value, CultureInfo.InvariantCulture), "false", StringComparison.OrdinalIgnoreCase);
                return texto + (tieneValor ? " tiene valor cargado" : " no tiene valor cargado");
            }

            return texto + " " + OpLabel(op) + " " + FormatValue(p.Value, p.LinkId, schema);
        }

        private static string OpLabel(string op)
        {
            switch (op)
            {
                case "=": return "es";
                case "!=": return "no es";
                case ">": return "mayor a";
                case "<": return "menor a";
                case ">=": return "mayor o igual a";
                case "<=": return "menor o igual a";
                case "in": return "es alguno de";
                case "contains": return "contiene";
                case "notcontains": return "no contiene";
                default: return op;
            }
        }

        /// <summary>
        /// El valor crudo del filtro -true/false, el código de una opción- no significa nada
        /// para quien lee el resumen; se resuelve a la misma etiqueta que ya usa el resto del
        /// reporte (Sí/No, el texto de la opción) usando el schema, no un mapeo propio.
        /// </summary>
        private static string FormatValue(object value, string linkId, ReportSchema schema)
        {
            if (value == null) return "";
            if (value is IEnumerable en && !(value is string))
                return string.Join(", ", en.Cast<object>().Select(v => FormatUnico(v, linkId, schema)));
            return FormatUnico(value, linkId, schema);
        }

        private static string FormatUnico(object value, string linkId, ReportSchema schema)
        {
            var texto = value?.ToString() ?? "";
            if (!schema.Knows(linkId)) return texto;

            switch (schema.TypeOf(linkId))
            {
                case QuestionType.Boolean:
                    return string.Equals(texto, "true", StringComparison.OrdinalIgnoreCase) ? "Sí" : "No";
                case QuestionType.Choice:
                case QuestionType.OpenChoice:
                    return schema.LabelFor(linkId, texto);
                default:
                    return texto;
            }
        }

        private static List<object> AsList(object value)
        {
            if (value == null) return new List<object>();
            if (value is string s) return new List<object> { s };
            if (value is IEnumerable en) return en.Cast<object>().ToList();
            return new List<object> { value };
        }
    }
}
