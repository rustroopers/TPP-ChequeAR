using System;
using System.Collections.Generic;
using System.Linq;
using ChequeAR.Domain.Entities;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Enums;
using Hl7.Fhir.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using DomainQuestionnaire = ChequeAR.Domain.Entities.Questionnaire;
using FhirQuestionnaire = Hl7.Fhir.Model.Questionnaire;

namespace ChequeAR.Application.Fhir
{
    /// <summary>
    /// Inverso de <see cref="FhirQuestionnaireBuilder"/>: reconstruye la entidad de dominio
    /// a partir del recurso FHIR que llega en una escritura.
    ///
    /// Es la pieza que permite que la interfaz tenga UNA sola representación. Todo lo que el
    /// builder emite tiene que volver acá sin pérdida: si un campo se emite pero no se
    /// reconstruye, desaparece en el próximo guardado y nadie se entera —no falla nada, el
    /// campo simplemente vuelve vacío—. Por eso el par está cubierto por pruebas de ida y
    /// vuelta sobre los 11 tipos de pregunta.
    /// </summary>
    public static class FhirQuestionnaireParser
    {
        private const string MinValueUrl = "http://hl7.org/fhir/StructureDefinition/minValue";
        private const string MaxValueUrl = "http://hl7.org/fhir/StructureDefinition/maxValue";
        private const string RegexUrl = "http://hl7.org/fhir/StructureDefinition/regex";
        private const string MimeTypeUrl = "http://hl7.org/fhir/StructureDefinition/mimeType";
        private const string UnitUrl = "http://hl7.org/fhir/StructureDefinition/questionnaire-unit";
        private const string ItemControlUrl = "http://hl7.org/fhir/StructureDefinition/questionnaire-itemControl";

        public static DomainQuestionnaire ToDomain(FhirQuestionnaire resource)
        {
            if (resource == null) return null;

            var q = new DomainQuestionnaire
            {
                Title = resource.Title ?? resource.Name,
                Description = resource.Description,
                SpecialtyCode = SpecialtyCode(resource),
                SpecialtyDisplay = SpecialtyDisplay(resource),
                CodingJson = ToQuestionnaireCodingJson(resource)
            };

            var orden = 0;
            var ordenGrupo = 0;
            foreach (var item in resource.Item ?? new List<FhirQuestionnaire.ItemComponent>())
            {
                if (EsGrupo(item))
                {
                    var grupo = new QuestionGroup
                    {
                        LinkId = item.LinkId,
                        Title = item.Text,
                        OrderIndex = ordenGrupo++
                    };
                    foreach (var hija in item.Item ?? new List<FhirQuestionnaire.ItemComponent>())
                        grupo.Questions.Add(ToQuestion(hija, orden++));
                    q.Groups.Add(grupo);
                }
                else
                {
                    q.Questions.Add(ToQuestion(item, orden++));
                }
            }
            return q;
        }

        private static bool EsGrupo(FhirQuestionnaire.ItemComponent item)
            => (item?.TypeElement?.ObjectValue as string) == "group";

        private static string SpecialtyCode(FhirQuestionnaire resource)
            => SpecialtyCoding(resource)?.Code;

        private static string SpecialtyDisplay(FhirQuestionnaire resource)
            => SpecialtyCoding(resource)?.Display;

        private static Coding SpecialtyCoding(FhirQuestionnaire resource)
        {
            var contexto = (resource.UseContext ?? new List<UsageContext>())
                .FirstOrDefault(u => (u.Value as CodeableConcept) != null);
            return (contexto?.Value as CodeableConcept)?.Coding?.FirstOrDefault();
        }

        private static Question ToQuestion(FhirQuestionnaire.ItemComponent item, int orderIndex)
        {
            var question = new Question
            {
                LinkId = item.LinkId,
                Text = item.Text,
                Type = ToQuestionType(item),
                Required = item.Required ?? false,
                // En una selección múltiple el repeats es parte del TIPO (ver
                // FhirQuestionnaireBuilder.RepeatsFor), no una decisión aparte de la variable.
                RepeatsAllowed = (item.Repeats ?? false) && ToQuestionType(item) != QuestionType.OpenChoice,
                OrderIndex = orderIndex,
                ValidationJson = ToValidationJson(item),
                EnableWhenJson = ToEnableWhenJson(item),
                CodingJson = ToCodingJson(item),
                VariableId = ChequeARExtensions.GetVariableId(item),
                // La versión que pide quien guarda; QuestionnaireService la resuelve contra el catálogo.
                VariableRevision = ChequeARExtensions.GetVariableVersion(item)
            };

            var ordenOpcion = 0;
            foreach (var opcion in item.AnswerOption ?? new List<FhirQuestionnaire.AnswerOptionComponent>())
            {
                var coding = opcion.Value as Coding;
                if (coding == null) continue;
                // El system distingue una opcion codificada de un codigo suelto: sin esto la
                // codificacion se emitiria en cada guardado y se perderia en cada lectura.
                var esSnomed = coding.System == TerminologyService.SnomedSystem;
                question.Options.Add(new AnswerOption
                {
                    Code = coding.Code,
                    Display = coding.Display ?? coding.Code,
                    Value = coding.Display ?? coding.Code,
                    SnomedCode = esSnomed ? coding.Code : null,
                    // sin la extensión, el display es el término
                    SnomedDisplay = esSnomed
                        ? ChequeARExtensions.GetString(coding, ChequeARExtensions.OptionSnomedDisplayUrl) ?? coding.Display
                        : null,
                    OrderIndex = ordenOpcion++
                });
            }
            return question;
        }

        /// <summary>
        /// Inverso de FhirQuestionnaireBuilder.FhirType. Acepta además el vocabulario R4 de
        /// lo publicado antes de la feature 009, por el mismo motivo que el reader.
        /// </summary>
        public static QuestionType ToQuestionType(FhirQuestionnaire.ItemComponent item)
        {
            var tipo = item?.TypeElement?.ObjectValue as string;
            var constraint = item?.AnswerConstraintElement?.ObjectValue as string;

            switch (tipo)
            {
                case "decimal":    return QuestionType.Decimal;
                case "string":     return QuestionType.String;
                case "text":       return QuestionType.Text;
                case "date":       return QuestionType.Date;
                case "dateTime":   return QuestionType.DateTime;
                case "boolean":    return QuestionType.Boolean;
                case "attachment": return QuestionType.Attachment;

                // FHIR no tiene tipo escala: es integer + itemControl slider.
                case "integer":
                    return HasItemControl(item, "slider") ? QuestionType.Scale : QuestionType.Integer;

                // Selección múltiple = repeats. optionsOrString es lo que declaraban las
                // versiones congeladas antes de corregir el mapeo, y significaba lo mismo.
                case "coding":
                    return item.Repeats == true || constraint == "optionsOrString"
                        ? QuestionType.OpenChoice
                        : QuestionType.Choice;

                // Legado R4.
                case "choice":      return QuestionType.Choice;
                case "open-choice": return QuestionType.OpenChoice;

                default: return QuestionType.String;
            }
        }

        /// <summary>
        /// Recompone el contrato JSON propio de validación desde las extensiones estándar y
        /// el maxLength nativo. Null si no hay ninguna restricción, para no guardar "{}".
        /// </summary>
        private static string ToValidationJson(FhirQuestionnaire.ItemComponent item)
        {
            var v = new JObject();

            foreach (var ext in item.Extension ?? new List<Extension>())
            {
                switch (ext.Url)
                {
                    case MinValueUrl: Set(v, "min", Numeric(ext.Value)); break;
                    case MaxValueUrl: Set(v, "max", Numeric(ext.Value)); break;
                    case UnitUrl: SetString(v, "unit", (ext.Value as Coding)?.Code); break;
                    case MimeTypeUrl: SetString(v, "accept", (ext.Value as Code)?.Value); break;
                    case RegexUrl: SetString(v, "pattern", (ext.Value as FhirString)?.Value); break;
                }
            }

            if (item.MaxLength.HasValue && item.MaxLength.Value > 0) v["maxLength"] = item.MaxLength.Value;

            return v.HasValues ? JsonConvert.SerializeObject(v, Formatting.None) : null;
        }

        private static string ToEnableWhenJson(FhirQuestionnaire.ItemComponent item)
        {
            var condicion = (item.EnableWhen ?? new List<FhirQuestionnaire.EnableWhenComponent>()).FirstOrDefault();
            if (condicion == null || string.IsNullOrWhiteSpace(condicion.Question)) return null;

            var o = new JObject
            {
                ["question"] = condicion.Question,
                ["op"] = (condicion.OperatorElement?.ObjectValue as string) ?? "="
            };

            switch (condicion.Answer)
            {
                case FhirBoolean b: o["value"] = b.Value ?? false; break;
                case Integer i: o["value"] = i.Value ?? 0; break;
                case FhirDecimal d: o["value"] = d.Value ?? 0m; break;
                case Coding c: o["value"] = c.Display ?? c.Code; break;
                case FhirString s: o["value"] = s.Value; break;
                case Date fecha: o["value"] = fecha.Value; break;
                case FhirDateTime momento: o["value"] = FhirQuestionnaireReader.LocalDateTime(momento.Value); break;
                default: o["value"] = null; break;
            }
            return JsonConvert.SerializeObject(o, Formatting.None);
        }

        /// <summary>
        /// El código clínico sale de item.code; el término de autoría, de la extensión propia.
        /// Son dos cosas distintas y pueden existir por separado: se puede anotar qué mide la
        /// pregunta antes de haber elegido un concepto.
        /// </summary>
        private static string ToCodingJson(FhirQuestionnaire.ItemComponent item)
        {
            var coding = (item.Code ?? new List<Coding>()).FirstOrDefault();
            var term = ChequeARExtensions.GetQuestionTerm(item);

            if (coding == null && string.IsNullOrWhiteSpace(term)) return null;

            var o = new JObject();
            if (coding != null && !string.IsNullOrWhiteSpace(coding.Code))
            {
                SetString(o, "system", coding.System);
                SetString(o, "code", coding.Code);
                SetString(o, "display", coding.Display);
            }
            SetString(o, "term", term);

            return o.HasValues ? JsonConvert.SerializeObject(o, Formatting.None) : null;
        }

        /// <summary>
        /// Inverso de <c>FhirQuestionnaireBuilder.AddQuestionnaireCoding</c>: el concepto sale
        /// de Questionnaire.code y el término de autoría de la extensión propia. Como en la
        /// pregunta, pueden existir por separado y devuelve null si no hay ninguno de los dos,
        /// para no guardar "{}".
        /// </summary>
        private static string ToQuestionnaireCodingJson(FhirQuestionnaire resource)
        {
            var coding = (resource.Code ?? new List<Coding>()).FirstOrDefault();
            var term = ChequeARExtensions.GetQuestionnaireTerm(resource);

            if (coding == null && string.IsNullOrWhiteSpace(term)) return null;

            var o = new JObject();
            if (coding != null && !string.IsNullOrWhiteSpace(coding.Code))
            {
                SetString(o, "system", coding.System);
                SetString(o, "code", coding.Code);
                SetString(o, "display", coding.Display);
            }
            SetString(o, "term", term);

            return o.HasValues ? JsonConvert.SerializeObject(o, Formatting.None) : null;
        }

        private static bool HasItemControl(FhirQuestionnaire.ItemComponent item, string code)
        {
            foreach (var ext in item.Extension ?? new List<Extension>())
            {
                if (ext.Url != ItemControlUrl) continue;
                if ((ext.Value as CodeableConcept)?.Coding?.Any(c => c.Code == code) == true) return true;
            }
            return false;
        }

        private static decimal? Numeric(DataType value)
        {
            if (value is Integer i) return i.Value;
            if (value is FhirDecimal d) return d.Value;
            return null;
        }

        private static void Set(JObject o, string key, decimal? value)
        {
            if (value.HasValue) o[key] = value.Value;
        }

        private static void SetString(JObject o, string key, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) o[key] = value;
        }
    }
}
