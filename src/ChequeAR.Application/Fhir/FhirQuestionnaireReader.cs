using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ChequeAR.Application.Fhir
{
    /// <summary>
    /// Inverso de <see cref="FhirQuestionnaireBuilder"/>: reconstruye la ficha a partir del
    /// JSON congelado de una versión publicada.
    ///
    /// Existe para que quien responde vea EXACTAMENTE la versión con la que empezó. Antes
    /// el renderer leía la entidad viva, así que una edición o una publicación en el medio
    /// le cambiaba las preguntas abajo de los pies.
    /// </summary>
    public static class FhirQuestionnaireReader
    {
        private const string MinValueUrl = "http://hl7.org/fhir/StructureDefinition/minValue";
        private const string MaxValueUrl = "http://hl7.org/fhir/StructureDefinition/maxValue";
        private const string UnitUrl = "http://hl7.org/fhir/StructureDefinition/questionnaire-unit";
        private const string MimeTypeUrl = "http://hl7.org/fhir/StructureDefinition/mimeType";
        private const string ItemControlUrl = "http://hl7.org/fhir/StructureDefinition/questionnaire-itemControl";

        public static RenderableQuestionnaire Read(string fhirJson)
        {
            if (string.IsNullOrWhiteSpace(fhirJson)) return null;

            JObject root;
            try { root = JObject.Parse(fhirJson); }
            catch (JsonException) { return null; }

            var result = new RenderableQuestionnaire
            {
                Title = (string)root["title"],
                Version = (string)root["version"]
            };

            var loose = new List<RenderableQuestion>();
            foreach (var item in (root["item"] as JArray) ?? new JArray())
            {
                if ((string)item["type"] == "group")
                {
                    var group = new RenderableGroup { LinkId = (string)item["linkId"], Title = (string)item["text"] };
                    foreach (var child in (item["item"] as JArray) ?? new JArray())
                        group.Questions.Add(ReadQuestion(child));
                    if (group.Questions.Count > 0) result.Groups.Add(group);
                }
                else
                {
                    loose.Add(ReadQuestion(item));
                }
            }

            if (loose.Count > 0)
            {
                result.Groups.Add(new RenderableGroup
                {
                    LinkId = "sin-grupo",
                    Title = result.Title ?? "Preguntas",
                    Questions = loose
                });
            }
            return result;
        }

        private static RenderableQuestion ReadQuestion(JToken item)
        {
            var question = new RenderableQuestion
            {
                LinkId = (string)item["linkId"],
                Text = (string)item["text"],
                Type = ReadType(item),
                Required = (bool?)item["required"] ?? false
            };

            foreach (var ext in (item["extension"] as JArray) ?? new JArray())
            {
                switch ((string)ext["url"])
                {
                    case MinValueUrl: question.Min = NumericValue(ext); break;
                    case MaxValueUrl: question.Max = NumericValue(ext); break;
                    case UnitUrl: question.Unit = (string)ext.SelectToken("valueCoding.code"); break;
                    case MimeTypeUrl: question.Accept = (string)ext["valueCode"]; break;
                }
            }

            foreach (var option in (item["answerOption"] as JArray) ?? new JArray())
            {
                var display = (string)option.SelectToken("valueCoding.display")
                           ?? (string)option.SelectToken("valueCoding.code");
                if (!string.IsNullOrEmpty(display)) question.Options.Add(display);
            }

            var condition = (item["enableWhen"] as JArray)?.FirstOrDefault();
            if (condition != null)
            {
                question.EnableWhen = new RenderableCondition
                {
                    Question = (string)condition["question"],
                    Op = (string)condition["operator"] ?? "=",
                    Value = ConditionValue(condition)
                };
            }
            return question;
        }

        // FHIR no tiene un tipo "scale": se modela como integer + itemControl slider.
        private static string ReadType(JToken item)
        {
            var type = (string)item["type"] ?? "string";
            if (type == "integer" && HasItemControl(item, "slider")) return "scale";
            if (type == "open-choice") return "openChoice";
            return type;
        }

        private static bool HasItemControl(JToken item, string code)
        {
            foreach (var ext in (item["extension"] as JArray) ?? new JArray())
            {
                if ((string)ext["url"] != ItemControlUrl) continue;
                foreach (var coding in (ext.SelectToken("valueCodeableConcept.coding") as JArray) ?? new JArray())
                    if ((string)coding["code"] == code) return true;
            }
            return false;
        }

        private static decimal? NumericValue(JToken ext)
        {
            return (decimal?)ext["valueInteger"] ?? (decimal?)ext["valueDecimal"];
        }

        private static object ConditionValue(JToken condition)
        {
            if (condition["answerBoolean"] != null) return (bool)condition["answerBoolean"];
            if (condition["answerInteger"] != null) return (long)condition["answerInteger"];
            if (condition["answerDecimal"] != null) return (decimal)condition["answerDecimal"];
            if (condition["answerCoding"] != null)
                return (string)condition.SelectToken("answerCoding.display") ?? (string)condition.SelectToken("answerCoding.code");
            return (string)condition["answerString"];
        }
    }

    public class RenderableQuestionnaire
    {
        public string Title { get; set; }
        public string Version { get; set; }
        public List<RenderableGroup> Groups { get; set; } = new List<RenderableGroup>();
    }

    public class RenderableGroup
    {
        public string LinkId { get; set; }
        public string Title { get; set; }
        public List<RenderableQuestion> Questions { get; set; } = new List<RenderableQuestion>();
    }

    public class RenderableQuestion
    {
        public string LinkId { get; set; }
        public string Text { get; set; }
        public string Type { get; set; }
        public bool Required { get; set; }
        public decimal? Min { get; set; }
        public decimal? Max { get; set; }
        public string Unit { get; set; }
        public string Accept { get; set; }
        public List<string> Options { get; set; } = new List<string>();
        public RenderableCondition EnableWhen { get; set; }
    }

    public class RenderableCondition
    {
        public string Question { get; set; }
        public string Op { get; set; }
        public object Value { get; set; }
    }
}
