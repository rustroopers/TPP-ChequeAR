using System;
using System.Collections.Generic;
using System.Linq;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ChequeAR.Application.Fhir
{
    /// <summary>
    /// Serializa un Questionnaire de dominio al recurso FHIR R5 que se congela en cada
    /// versión publicada. La UI publica desde el listado (sin pasar por el editor), así
    /// que el JSON tiene que poder armarse enteramente desde la base.
    /// </summary>
    public static class FhirQuestionnaireBuilder
    {
        public const string ProfileUrl = "https://chequear.garrahan/StructureDefinition/ChequeARQuestionnaire";

        // Índice linkId -> pregunta, para resolver el tipo del answer[x] de enableWhen.
        // [ThreadStatic] porque la clase es estática y Build puede correr en paralelo.
        [ThreadStatic] private static Dictionary<string, Question> _questionsByLinkId;

        public static string Build(Questionnaire q, int versionNumber)
        {
            if (q == null) throw new ArgumentNullException(nameof(q));

            var resource = new JObject
            {
                ["resourceType"] = "Questionnaire",
                ["id"] = q.Id.ToString(),
                ["meta"] = new JObject { ["profile"] = new JArray(ProfileUrl) },
                ["url"] = "https://chequear.garrahan/Questionnaire/" + q.Id,
                ["version"] = versionNumber.ToString(),
                ["title"] = q.Title,
                ["name"] = q.Title,
                ["status"] = "active",
                ["subjectType"] = new JArray("Patient"),
                ["date"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["publisher"] = "Hospital Garrahan"
            };
            if (!string.IsNullOrWhiteSpace(q.Description)) resource["description"] = q.Description;
            if (!string.IsNullOrWhiteSpace(q.SpecialtyCode))
            {
                resource["useContext"] = new JArray(new JObject
                {
                    ["code"] = new JObject { ["system"] = "http://terminology.hl7.org/CodeSystem/usage-context-type", ["code"] = "focus" },
                    ["valueCodeableConcept"] = new JObject
                    {
                        ["coding"] = new JArray(new JObject
                        {
                            ["system"] = "https://chequear.garrahan/CodeSystem/specialty",
                            ["code"] = q.SpecialtyCode,
                            ["display"] = q.SpecialtyDisplay ?? q.SpecialtyCode
                        })
                    }
                });
            }

            _questionsByLinkId = (q.Questions ?? new List<Question>())
                .Where(x => !string.IsNullOrWhiteSpace(x.LinkId))
                .GroupBy(x => x.LinkId)
                .ToDictionary(g => g.Key, g => g.First());

            resource["item"] = BuildItems(q);
            return JsonConvert.SerializeObject(resource, Formatting.None);
        }

        private static JArray BuildItems(Questionnaire q)
        {
            var items = new JArray();
            var grouped = new HashSet<Guid>();

            var groups = (q.Groups ?? new List<QuestionGroup>()).OrderBy(g => g.OrderIndex);
            foreach (var g in groups)
            {
                // Las preguntas llegan por Questionnaire.Questions (con GroupId), no por
                // Group.Questions: el repo sólo hace Include de la colección plana.
                var children = (q.Questions ?? new List<Question>())
                    .Where(x => x.GroupId == g.Id)
                    .OrderBy(x => x.OrderIndex)
                    .ToList();
                foreach (var c in children) grouped.Add(c.Id);

                items.Add(new JObject
                {
                    ["linkId"] = string.IsNullOrWhiteSpace(g.LinkId) ? g.Id.ToString() : g.LinkId,
                    ["text"] = g.Title,
                    ["type"] = "group",
                    ["item"] = new JArray(children.Select(BuildQuestion).Cast<object>().ToArray())
                });
            }

            var loose = (q.Questions ?? new List<Question>())
                .Where(x => !grouped.Contains(x.Id))
                .OrderBy(x => x.OrderIndex);
            foreach (var question in loose) items.Add(BuildQuestion(question));

            return items;
        }

        private static JObject BuildQuestion(Question question)
        {
            var item = new JObject
            {
                ["linkId"] = string.IsNullOrWhiteSpace(question.LinkId) ? question.Id.ToString() : question.LinkId,
                ["text"] = question.Text,
                ["type"] = FhirType(question.Type),
                ["required"] = question.Required
            };
            if (question.RepeatsAllowed) item["repeats"] = true;
            AddValidationExtensions(item, question);
            AddCoding(item, question);
            AddEnableWhen(item, question);

            var options = (question.Options ?? new List<AnswerOption>()).OrderBy(o => o.OrderIndex).ToList();
            if (options.Count > 0)
            {
                item["answerOption"] = new JArray(options.Select(o => (object)new JObject
                {
                    ["valueCoding"] = new JObject
                    {
                        ["code"] = o.Code ?? o.Value ?? o.Display,
                        ["display"] = o.Display ?? o.Value
                    }
                }).ToArray());
            }
            return item;
        }

        /// <summary>Metadata clínica (CIE-10 / SNOMED / LOINC) como Questionnaire.item.code.</summary>
        private static void AddCoding(JObject item, Question question)
        {
            var coding = TryParse(question.CodingJson);
            if (coding == null) return;
            var code = (string)coding["code"];
            if (string.IsNullOrWhiteSpace(code)) return;

            item["code"] = new JArray(new JObject
            {
                ["system"] = (string)coding["system"],
                ["code"] = code,
                ["display"] = (string)coding["display"]
            });
        }

        /// <summary>
        /// Lógica condicional como Questionnaire.item.enableWhen. El tipo del answer[x]
        /// tiene que corresponder al de la pregunta referenciada, no al de esta.
        /// </summary>
        private static void AddEnableWhen(JObject item, Question question)
        {
            var condition = TryParse(question.EnableWhenJson);
            if (condition == null) return;
            var sourceLinkId = (string)condition["question"];
            if (string.IsNullOrWhiteSpace(sourceLinkId)) return;

            var entry = new JObject
            {
                ["question"] = sourceLinkId,
                ["operator"] = (string)condition["op"] ?? "="
            };

            var value = condition["value"];
            Question source = null;
            _questionsByLinkId?.TryGetValue(sourceLinkId, out source);
            switch (source?.Type)
            {
                case QuestionType.Boolean: entry["answerBoolean"] = (bool?)value ?? false; break;
                case QuestionType.Integer:
                case QuestionType.Scale:   entry["answerInteger"] = (long?)value ?? 0; break;
                case QuestionType.Decimal: entry["answerDecimal"] = (decimal?)value ?? 0m; break;
                case QuestionType.Choice:
                case QuestionType.OpenChoice:
                    entry["answerCoding"] = new JObject { ["code"] = (string)value, ["display"] = (string)value };
                    break;
                default: entry["answerString"] = (string)value; break;
            }

            item["enableWhen"] = new JArray(entry);
            // Con una sola condición el behavior es irrelevante, pero declararlo deja el
            // recurso listo para cuando se soporte más de una.
            item["enableBehavior"] = "all";
        }

        private static JObject TryParse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JObject.Parse(json); }
            catch (JsonException) { return null; }
        }

        /// <summary>
        /// Rango y unidad como extensiones estándar de FHIR, en vez de inventar un formato
        /// propio: así la versión congelada sigue siendo interpretable por otro sistema.
        /// </summary>
        private static void AddValidationExtensions(JObject item, Question question)
        {
            if (string.IsNullOrWhiteSpace(question.ValidationJson)) return;

            var validation = TryParse(question.ValidationJson);
            if (validation == null) return;   // ausente o corrupto: se publica sin restricciones

            var extensions = new JArray();
            var isInteger = question.Type == QuestionType.Integer;

            var min = validation["min"];
            if (min != null && min.Type != JTokenType.Null)
                extensions.Add(ValueExtension("http://hl7.org/fhir/StructureDefinition/minValue", min, isInteger));

            var max = validation["max"];
            if (max != null && max.Type != JTokenType.Null)
                extensions.Add(ValueExtension("http://hl7.org/fhir/StructureDefinition/maxValue", max, isInteger));

            var unit = (string)validation["unit"];
            if (!string.IsNullOrWhiteSpace(unit))
            {
                extensions.Add(new JObject
                {
                    ["url"] = "http://hl7.org/fhir/StructureDefinition/questionnaire-unit",
                    ["valueCoding"] = new JObject
                    {
                        ["system"] = "http://unitsofmeasure.org",
                        ["code"] = unit,
                        ["display"] = unit
                    }
                });
            }

            if (extensions.Count > 0) item["extension"] = extensions;
        }

        private static JObject ValueExtension(string url, JToken value, bool isInteger)
        {
            var ext = new JObject { ["url"] = url };
            // El tipo del value[x] tiene que acompañar al de la pregunta: valueInteger para
            // integer, valueDecimal para decimal.
            if (isInteger) ext["valueInteger"] = (long)value;
            else ext["valueDecimal"] = (decimal)value;
            return ext;
        }

        private static string FhirType(QuestionType type)
        {
            switch (type)
            {
                case QuestionType.DateTime: return "dateTime";
                case QuestionType.OpenChoice: return "open-choice";
                case QuestionType.Scale: return "integer";   // FHIR no tiene "scale": se modela como integer
                default: return type.ToString().ToLowerInvariant();
            }
        }
    }
}
