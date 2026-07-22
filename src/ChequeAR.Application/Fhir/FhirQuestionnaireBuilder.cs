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

        /// <summary>
        /// Rango y unidad como extensiones estándar de FHIR, en vez de inventar un formato
        /// propio: así la versión congelada sigue siendo interpretable por otro sistema.
        /// </summary>
        private static void AddValidationExtensions(JObject item, Question question)
        {
            if (string.IsNullOrWhiteSpace(question.ValidationJson)) return;

            JObject validation;
            try { validation = JObject.Parse(question.ValidationJson); }
            catch (JsonException) { return; }   // JSON corrupto: se publica sin restricciones

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
