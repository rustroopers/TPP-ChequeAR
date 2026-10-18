using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ChequeAR.Application.Services
{
    /// <summary>
    /// Un Questionnaire FHIR como líneas legibles para un clínico: título, descripción,
    /// especialidad, secciones y preguntas con su tipo, si son obligatorias, su concepto SNOMED,
    /// su condición y sus opciones. Sirve para mostrar qué cambió entre dos estados: una
    /// publicación contra la versión anterior, o un guardado del borrador contra lo que había.
    /// </summary>
    public static class QuestionnaireReadableLines
    {
        public static List<string> From(string fhirJson)
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
            var especialidad = (string)root.SelectToken("useContext[0].valueCodeableConcept.coding[0].display")
                               ?? (string)root.SelectToken("useContext[0].valueCodeableConcept.coding[0].code");
            if (!string.IsNullOrWhiteSpace(especialidad)) lines.Add("Especialidad: " + especialidad);
            var concepto = Concept(root["code"] as JArray);
            if (concepto != null) lines.Add("Concepto SNOMED: " + concepto);

            var textos = new Dictionary<string, string>();
            Index(root["item"] as JArray, textos);

            if (root["item"] is JArray items)
                foreach (var it in items) FlattenItem(it as JObject, lines, "", textos);

            return lines;
        }

        private static void Index(JArray items, Dictionary<string, string> textos)
        {
            if (items == null) return;
            foreach (var it in items.OfType<JObject>())
            {
                var linkId = (string)it["linkId"];
                if (linkId != null && !textos.ContainsKey(linkId)) textos[linkId] = (string)it["text"];
                Index(it["item"] as JArray, textos);
            }
        }

        private static void FlattenItem(JObject item, List<string> lines, string indent, Dictionary<string, string> textos)
        {
            if (item == null) return;
            var type = (string)item["type"];
            var text = (string)item["text"];

            if (type == "group")
            {
                lines.Add(indent + "Sección: " + (text ?? "(sin título)"));
                if (item["item"] is JArray children)
                    foreach (var c in children) FlattenItem(c as JObject, lines, indent + "    ", textos);
                return;
            }

            var required = item["required"] != null && (bool)item["required"];
            lines.Add(indent + "Pregunta: " + (text ?? "(sin texto)")
                      + " [" + TypeLabel(type, (string)item["answerConstraint"], item["repeats"]?.Type == JTokenType.Boolean && (bool)item["repeats"])
                      + (required ? ", obligatoria" : ", opcional") + "]");

            var concepto = Concept(item["code"] as JArray);
            if (concepto != null) lines.Add(indent + "    Concepto SNOMED: " + concepto);

            if (item["enableWhen"] is JArray condiciones)
                foreach (var c in condiciones.OfType<JObject>())
                    lines.Add(indent + "    Se muestra si: " + Condition(c, textos));

            if (item["answerOption"] is JArray opts)
                foreach (var o in opts)
                {
                    var display = (string)(o?["valueCoding"]?["display"]) ?? (string)(o?["valueCoding"]?["code"]);
                    if (!string.IsNullOrWhiteSpace(display)) lines.Add(indent + "    • Opción: " + display);
                }
        }

        private static string Concept(JArray codes)
        {
            var c = codes?.OfType<JObject>().FirstOrDefault();
            var code = (string)c?["code"];
            if (string.IsNullOrWhiteSpace(code)) return null;
            var display = (string)c["display"];
            return string.IsNullOrWhiteSpace(display) ? code : display + " (" + code + ")";
        }

        private static string Condition(JObject c, Dictionary<string, string> textos)
        {
            var linkId = (string)c["question"];
            var pregunta = linkId != null && textos.TryGetValue(linkId, out var t) && !string.IsNullOrWhiteSpace(t) ? t : linkId;
            var valor = c.Properties().FirstOrDefault(p => p.Name.StartsWith("answer", StringComparison.Ordinal))?.Value;
            var texto = valor is JObject coding ? (string)coding["display"] ?? (string)coding["code"] : valor?.ToString();
            if (valor?.Type == JTokenType.Boolean) texto = (bool)valor ? "Sí" : "No";
            return "«" + pregunta + "» " + OperatorLabel((string)c["operator"]) + " " + texto;
        }

        private static string OperatorLabel(string op)
        {
            switch (op)
            {
                case "=": return "es";
                case "!=": return "no es";
                case ">": return "mayor que";
                case "<": return "menor que";
                case ">=": return "mayor o igual que";
                case "<=": return "menor o igual que";
                case "exists": return "tiene respuesta:";
                default: return op;
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
    }
}
