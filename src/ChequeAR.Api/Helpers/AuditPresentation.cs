using System;
using System.Collections.Generic;
using System.Linq;
using ChequeAR.Application.Dtos;
using Newtonsoft.Json.Linq;

namespace ChequeAR.Api.Helpers
{
    /// <summary>
    /// Traduce un evento de auditoría (código C/R/U/D/E + referencia FHIR) a algo legible
    /// para un clínico: una etiqueta en español, un color y un ícono. También parsea la
    /// referencia para saber a qué recurso apunta y qué operación fue.
    /// </summary>
    public static class AuditPresentation
    {
        /// <summary>Operación de negocio, más fina que el código C/R/U/D.</summary>
        public enum Op { Create, Edit, Publish, Archive, Clone, Delete, Read, Finalize, Export, Other }

        public class ActionView
        {
            public string Label { get; set; }        // "Nueva versión publicada"
            public string BadgeClass { get; set; }   // "cq-badge--published"
            public string Icon { get; set; }         // "bi-send-check"
            public string ResourceType { get; set; } // "Questionnaire"
            public Guid? ResourceId { get; set; }
            public Op Operation { get; set; }
        }

        /// <summary>Parte la referencia "Tipo/{id}$sufijo" en (tipo, id, sufijo).</summary>
        public static void ParseRef(string entityRef, out string type, out Guid? id, out string suffix)
        {
            type = null; id = null; suffix = null;
            if (string.IsNullOrWhiteSpace(entityRef)) return;

            var dollar = entityRef.IndexOf('$');
            var head = dollar >= 0 ? entityRef.Substring(0, dollar) : entityRef;
            suffix = dollar >= 0 ? entityRef.Substring(dollar + 1) : null;

            var slash = head.IndexOf('/');
            if (slash < 0) { type = head; return; }
            type = head.Substring(0, slash);
            var idPart = head.Substring(slash + 1);
            if (Guid.TryParse(idPart, out var g)) id = g;
        }

        /// <summary>Cuestionario de un evento de reporte ("Report/…?questionnaire={id}&amp;…"), si lo trae.</summary>
        public static Guid? ReportQuestionnaireId(string entityRef)
        {
            const string key = "questionnaire=";
            if (entityRef == null || !entityRef.StartsWith("Report/", StringComparison.Ordinal)) return null;
            var at = entityRef.IndexOf(key, StringComparison.Ordinal);
            if (at < 0) return null;
            var value = entityRef.Substring(at + key.Length);
            var amp = value.IndexOf('&');
            if (amp >= 0) value = value.Substring(0, amp);
            return Guid.TryParse(value, out var id) ? id : (Guid?)null;
        }

        /// <summary>Historia clínica de un evento de paciente ("Patient/{hc}$operación").</summary>
        public static string PatientHospitalId(string entityRef)
        {
            const string prefix = "Patient/";
            if (entityRef == null || !entityRef.StartsWith(prefix, StringComparison.Ordinal)) return null;
            var value = entityRef.Substring(prefix.Length);
            var dollar = value.LastIndexOf('$');
            return dollar >= 0 ? value.Substring(0, dollar) : value;
        }

        public static ActionView Describe(string entityRef, string actionCode)
        {
            ParseRef(entityRef, out var type, out var id, out var suffix);
            var view = new ActionView { ResourceType = type, ResourceId = id, Operation = Op.Other };

            if (type == "Questionnaire")
            {
                if (StartsWith(suffix, "publish")) return Set(view, Op.Publish, "Nueva versión publicada", "cq-badge--published", "bi-send-check");
                if (StartsWith(suffix, "archive")) return Set(view, Op.Archive, "Cuestionario archivado", "cq-badge--archived", "bi-archive");
                if (StartsWith(suffix, "clone")) return Set(view, Op.Clone, "Cuestionario clonado", "cq-badge--info", "bi-files");
                switch (actionCode)
                {
                    case "C": return Set(view, Op.Create, "Cuestionario creado", "cq-badge--published", "bi-plus-circle");
                    case "U": return Set(view, Op.Edit, "Cuestionario editado", "cq-badge--info", "bi-pencil");
                    case "D": return Set(view, Op.Delete, "Cuestionario eliminado", "cq-badge--danger", "bi-trash");
                    case "R": return Set(view, Op.Read, "Cuestionario consultado", "cq-badge--archived", "bi-eye");
                }
            }
            else if (type == "QuestionnaireResponse")
            {
                if (StartsWith(suffix, "finalize")) return Set(view, Op.Finalize, "Respuesta finalizada", "cq-badge--published", "bi-check2-circle");
                if (StartsWith(suffix, "save")) return Set(view, Op.Edit, "Avance guardado", "cq-badge--info", "bi-clock-history");
                if (StartsWith(suffix, "retarget")) return Set(view, Op.Edit, "Versión actualizada", "cq-badge--info", "bi-arrow-up-circle");
                if (StartsWith(suffix, "abandon")) return Set(view, Op.Delete, "Ficha descartada", "cq-badge--danger", "bi-x-circle");
                if (StartsWith(suffix, "lock")) return Set(view, Op.Other, "Edición iniciada", "cq-badge--archived", "bi-lock");
                // "$unlock" ya no se emite como evento propio (el cierre audita "$save" con el
                // diff); se deja el mapeo para eventos viejos guardados en la base.
                if (StartsWith(suffix, "unlock")) return Set(view, Op.Other, "Edición cerrada", "cq-badge--archived", "bi-unlock");
                switch (actionCode)
                {
                    case "C": return Set(view, Op.Create, "Respuesta iniciada", "cq-badge--info", "bi-clipboard-plus");
                    case "R": return Set(view, Op.Read, "Respuesta consultada", "cq-badge--archived", "bi-eye");
                    case "U": return Set(view, Op.Edit, "Respuesta modificada", "cq-badge--info", "bi-pencil");
                }
            }
            else if (type == "Report")
            {
                // "Report/statsExport?…" son las estadísticas; el resto, la tabla.
                var stats = entityRef.StartsWith("Report/statsExport", StringComparison.Ordinal);
                return Set(view, Op.Export, stats ? "Estadísticas exportadas" : "Reporte exportado", "cq-badge--info", "bi-download");
            }
            else if (type == "Patient" && StartsWith(suffix, "trend"))
            {
                return Set(view, Op.Read, "Evolución consultada", "cq-badge--archived", "bi-graph-up");
            }
            else if (type == "AuditEvent" && actionCode == "E")
            {
                return Set(view, Op.Export, "Auditoría exportada", "cq-badge--info", "bi-download");
            }
            else if (type == "SavedReport")
            {
                switch (actionCode)
                {
                    case "C": return Set(view, Op.Create, "Reporte creado", "cq-badge--published", "bi-file-earmark-bar-graph");
                    case "U": return Set(view, Op.Edit, "Reporte modificado", "cq-badge--info", "bi-pencil");
                    case "D": return Set(view, Op.Delete, "Reporte eliminado", "cq-badge--danger", "bi-trash");
                }
            }

            // Fallback genérico por código, en español.
            switch (actionCode)
            {
                case "C": return Set(view, Op.Create, "Creación", "cq-badge--published", "bi-plus-circle");
                case "R": return Set(view, Op.Read, "Consulta", "cq-badge--archived", "bi-eye");
                case "U": return Set(view, Op.Edit, "Modificación", "cq-badge--info", "bi-pencil");
                case "D": return Set(view, Op.Delete, "Eliminación", "cq-badge--danger", "bi-trash");
                case "E": return Set(view, Op.Export, "Ejecución", "cq-badge--info", "bi-lightning");
                default: return Set(view, Op.Other, actionCode ?? "—", "cq-badge--archived", "bi-question-circle");
            }
        }

        private static bool StartsWith(string s, string prefix) =>
            !string.IsNullOrEmpty(s) && s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

        private static ActionView Set(ActionView v, Op op, string label, string badge, string icon)
        {
            v.Operation = op; v.Label = label; v.BadgeClass = badge; v.Icon = icon;
            return v;
        }

        // ===== Diff legible entre dos versiones publicadas =====

        /// <summary>
        /// Aplana un Questionnaire FHIR (el JSON congelado de una versión) a una lista de
        /// líneas legibles para un clínico: título, descripción, secciones y preguntas con
        /// su tipo y si son obligatorias.
        /// </summary>
        public static List<string> FlattenForHumans(string fhirJson)
        {
            var lines = new List<string>();
            if (string.IsNullOrWhiteSpace(fhirJson)) return lines;

            JObject root;
            try { root = JObject.Parse(fhirJson); }
            catch (Newtonsoft.Json.JsonException) { return lines; }   // versión ilegible: sin detalle

            var title = (string)root["title"];
            if (!string.IsNullOrWhiteSpace(title)) lines.Add("Título: " + title);
            var desc = (string)root["description"];
            if (!string.IsNullOrWhiteSpace(desc)) lines.Add("Descripción: " + desc);

            if (root["item"] is JArray items)
                foreach (var it in items) FlattenItem(it as JObject, lines, "");

            return lines;
        }

        private static void FlattenItem(JObject item, List<string> lines, string indent)
        {
            if (item == null) return;
            var type = (string)item["type"];
            var text = (string)item["text"];

            if (type == "group")
            {
                lines.Add(indent + "Sección: " + (text ?? "(sin título)"));
                if (item["item"] is JArray children)
                    foreach (var c in children) FlattenItem(c as JObject, lines, indent + "    ");
                return;
            }

            var required = item["required"] != null && (bool)item["required"];
            lines.Add(indent + "Pregunta: " + (text ?? "(sin texto)")
                      + " [" + TypeLabel(type, (string)item["answerConstraint"], item["repeats"]?.Type == JTokenType.Boolean && (bool)item["repeats"])
                      + (required ? ", obligatoria" : ", opcional") + "]");

            if (item["answerOption"] is JArray opts)
                foreach (var o in opts)
                {
                    var display = (string)(o?["valueCoding"]?["display"]) ?? (string)(o?["valueCoding"]?["code"]);
                    if (!string.IsNullOrWhiteSpace(display)) lines.Add(indent + "    • Opción: " + display);
                }
        }

        /// <summary>
        /// Etiqueta legible por un clínico. Tiene que cubrir DOS vocabularios a la vez:
        ///
        ///  - R5: las preguntas de opción son 'coding'; la selección múltiple es la que repite
        ///    (o, en lo congelado antes de corregir ese mapeo, la que declara optionsOrString).
        ///  - R4 ('choice' / 'open-choice'): lo que quedó congelado en las versiones
        ///    publicadas antes, que son inmutables y se siguen mostrando en auditoría.
        ///
        /// Sin el caso 'coding' esta pantalla mostraría el literal crudo de FHIR a un médico.
        /// </summary>
        private static string TypeLabel(string fhirType, string answerConstraint = null, bool repeats = false)
        {
            switch (fhirType)
            {
                case "string": return "texto";
                case "text": return "texto largo";
                case "integer": return "número";
                case "decimal": return "número decimal";
                case "boolean": return "sí/no";
                case "date": return "fecha";
                case "dateTime": return "fecha y hora";
                case "attachment": return "adjunto";

                case "coding":
                    return repeats || answerConstraint == "optionsOrString" ? "opción múltiple" : "opción única";

                // Legado R4 de lo ya congelado.
                case "choice": return "opción única";
                case "open-choice": return "opción múltiple";

                default: return fhirType ?? "—";
            }
        }

        public enum Change { Same, Added, Removed }

        public class DiffLine
        {
            public string Text { get; set; }
            public Change Change { get; set; }
        }

        /// <summary>
        /// Marca cada línea de "después" como agregada o igual, y cada línea de "antes" como
        /// quitada o igual, comparando por pertenencia. Suficiente para un "así era / así quedó".
        /// </summary>
        public static void Diff(List<string> before, List<string> after,
                                out List<DiffLine> beforeMarked, out List<DiffLine> afterMarked)
        {
            var beforeSet = new HashSet<string>(before);
            var afterSet = new HashSet<string>(after);

            beforeMarked = before.Select(l => new DiffLine
            {
                Text = l,
                Change = afterSet.Contains(l) ? Change.Same : Change.Removed
            }).ToList();

            afterMarked = after.Select(l => new DiffLine
            {
                Text = l,
                Change = beforeSet.Contains(l) ? Change.Same : Change.Added
            }).ToList();
        }

        // ===== Consulta guardada (SavedReport): foto legible de sus condiciones =====

        /// <summary>Lee la foto de una consulta guardada serializada en el evento. Null si no es interpretable.</summary>
        public static Application.Services.SavedReportAuditSnapshot ReadSavedReport(string fhirJson)
        {
            if (string.IsNullOrWhiteSpace(fhirJson)) return null;
            try
            {
                return Newtonsoft.Json.JsonConvert
                    .DeserializeObject<Application.Services.SavedReportAuditSnapshot>(fhirJson);
            }
            catch (Newtonsoft.Json.JsonException) { return null; }
        }

        /// <summary>
        /// Traduce la definición de la consulta (filtros, fechas, versiones, autores) a líneas
        /// que un clínico pueda leer, resolviendo cada linkId al texto de su pregunta. Devuelve
        /// lista vacía si la consulta no tiene condiciones (equivale a "todas las fichas").
        /// </summary>
        public static List<string> DescribeReportConditions(string queryJson, IReadOnlyDictionary<string, string> linkTexts)
        {
            var lines = new List<string>();
            if (string.IsNullOrWhiteSpace(queryJson)) return lines;

            ReportQuery query;
            // Newtonsoft matchea las propiedades sin distinguir mayúsculas, así que el JSON
            // camelCase que mandó la isla se deserializa sin resolver adicional.
            try { query = Newtonsoft.Json.JsonConvert.DeserializeObject<ReportQuery>(queryJson); }
            catch (Newtonsoft.Json.JsonException) { return lines; }
            if (query == null) return lines;

            if (query.QuestionnaireVersions != null && query.QuestionnaireVersions.Count > 0)
                lines.Add("Versión del cuestionario: " + string.Join(", ", query.QuestionnaireVersions));

            if (query.DateRange != null && (query.DateRange.From.HasValue || query.DateRange.To.HasValue))
            {
                var from = query.DateRange.From.HasValue ? query.DateRange.From.Value.ToString("dd/MM/yyyy") : "el inicio";
                var to = query.DateRange.To.HasValue ? query.DateRange.To.Value.ToString("dd/MM/yyyy") : "hoy";
                lines.Add("Fecha de carga: entre " + from + " y " + to);
            }

            var authors = (query.Authors ?? new List<string>())
                .Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
            if (authors.Count > 0)
                lines.Add("Cargadas por: " + string.Join(", ", authors));

            if (query.Filters?.Predicates != null && query.Filters.Predicates.Count > 0)
                FlattenFilterNode(query.Filters, linkTexts, lines, 0);

            return lines;
        }

        private static void FlattenFilterNode(FilterNode node, IReadOnlyDictionary<string, string> linkTexts,
                                              List<string> lines, int depth)
        {
            if (node?.Predicates == null || node.Predicates.Count == 0) return;
            var indent = new string(' ', depth * 4);

            if (node.Predicates.Count > 1)
            {
                var connector = string.Equals(node.Logic, "OR", StringComparison.OrdinalIgnoreCase)
                    ? "Se cumple ALGUNA de estas condiciones:"
                    : "Se cumplen TODAS estas condiciones:";
                lines.Add(indent + connector);
            }

            foreach (var p in node.Predicates)
            {
                if (p.Group != null) { FlattenFilterNode(p.Group, linkTexts, lines, depth + 1); continue; }

                var text = linkTexts != null && p.LinkId != null
                           && linkTexts.TryGetValue(p.LinkId, out var t) && !string.IsNullOrWhiteSpace(t)
                    ? t : (p.LinkId ?? "campo");
                lines.Add(indent + "• " + text + " " + OpPhrase(p.Op, p.Value));
            }
        }

        /// <summary>Frase del operador + valor, en español.</summary>
        private static string OpPhrase(string op, object value)
        {
            switch ((op ?? string.Empty).ToLowerInvariant())
            {
                case "exists":
                    return string.Equals(FormatValue(value), "false", StringComparison.OrdinalIgnoreCase) || string.Equals(FormatValue(value), "No", StringComparison.Ordinal)
                        ? "no fue respondida" : "fue respondida";
                case "contains": return "contiene «" + FormatValue(value) + "»";
                case "notcontains": return "no contiene «" + FormatValue(value) + "»";
                case "=":        return "es igual a " + FormatValue(value);
                case "!=":       return "es distinto de " + FormatValue(value);
                case ">":        return "es mayor que " + FormatValue(value);
                case "<":        return "es menor que " + FormatValue(value);
                case ">=":       return "es mayor o igual que " + FormatValue(value);
                case "<=":       return "es menor o igual que " + FormatValue(value);
                case "in":       return "es alguno de " + string.Join(", ", AsValueList(value).Select(FormatScalar));
                case "between":
                    var vs = AsValueList(value);
                    return vs.Count == 2 ? "está entre " + FormatScalar(vs[0]) + " y " + FormatScalar(vs[1])
                                         : "está en el rango " + string.Join(", ", vs.Select(FormatScalar));
                default:         return (op ?? "?") + " " + FormatValue(value);
            }
        }

        private static string FormatValue(object value)
        {
            var list = value as JArray;
            if (list != null) return string.Join(", ", list.Select(FormatScalar));
            return FormatScalar(value);
        }

        private static string FormatScalar(object value)
        {
            if (value == null) return "—";
            if (value is JValue jv) value = jv.Value;
            if (value is bool b) return b ? "Sí" : "No";
            return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static List<object> AsValueList(object value)
        {
            if (value is JArray arr) return arr.Cast<object>().ToList();
            if (value == null) return new List<object>();
            return new List<object> { value };
        }
    }
}
