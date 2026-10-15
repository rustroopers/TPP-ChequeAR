using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using FhirQuestionnaire = Hl7.Fhir.Model.Questionnaire;

namespace ChequeAR.Application.Fhir
{
    /// <summary>
    /// Inverso de <see cref="FhirQuestionnaireBuilder"/>: reconstruye la ficha a partir del
    /// JSON congelado de una versión publicada.
    ///
    /// Existe para que quien responde vea EXACTAMENTE la versión con la que empezó. Antes el
    /// renderizador leía la entidad viva, así que una edición o una publicación en el medio
    /// le cambiaba las preguntas abajo de los pies.
    ///
    /// El contrato de salida (Renderable*) NO cambia con la feature 009: lo consumen la
    /// hidratación del renderizador y QuestionnaireResponseService, y mantenerlo intacto es
    /// lo que evita tocar esas rutas.
    /// </summary>
    public static class FhirQuestionnaireReader
    {
        private const string MinValueUrl = "http://hl7.org/fhir/StructureDefinition/minValue";
        private const string MaxValueUrl = "http://hl7.org/fhir/StructureDefinition/maxValue";
        private const string UnitUrl = "http://hl7.org/fhir/StructureDefinition/questionnaire-unit";
        private const string MimeTypeUrl = "http://hl7.org/fhir/StructureDefinition/mimeType";
        private const string RegexUrl = "http://hl7.org/fhir/StructureDefinition/regex";
        private const string ItemControlUrl = "http://hl7.org/fhir/StructureDefinition/questionnaire-itemControl";

        /// <summary>
        /// Parser PERMISIVO, a propósito. Las versiones publicadas antes de la feature 009
        /// llevan vocabulario R4 ('choice', 'open-choice') que R5 eliminó, y son inmutables:
        /// hay fichas clínicas que las referencian. Con un parser estricto, 9 de las 20
        /// versiones publicadas dejarían de renderizarse — regresión de FR-014 y FR-023.
        ///
        /// La asimetría es deliberada: permisivo al LEER lo viejo, estricto al ESCRIBIR lo
        /// nuevo. Nada de lo que se publique de acá en adelante depende de esta tolerancia.
        /// </summary>
        private static readonly FhirJsonParser Parser = new FhirJsonParser(new ParserSettings
        {
            AcceptUnknownMembers = true,
            AllowUnrecognizedEnums = true,
            PermissiveParsing = true
        });

        /// <summary>
        /// Igual que <see cref="Read"/>, pero devuelve la entidad de dominio: lo usan la vista
        /// previa de una versión puntual y la comparación entre versiones, que necesitan la
        /// misma forma que la entidad viva (grupos con Id, preguntas con GroupId) para reusar
        /// el armado de pantalla del cuestionario actual.
        ///
        /// Parser permisivo por el mismo motivo que Read: las versiones viejas son inmutables.
        /// </summary>
        public static ChequeAR.Domain.Entities.Questionnaire ReadDomain(string fhirJson)
        {
            if (string.IsNullOrWhiteSpace(fhirJson)) return null;

            FhirQuestionnaire resource;
            try { resource = Parser.Parse<FhirQuestionnaire>(fhirJson); }
            catch (Exception) { return null; }
            var q = FhirQuestionnaireParser.ToDomain(resource);
            if (q == null) return null;

            // El parser cuelga las preguntas de cada grupo; la entidad persistida las tiene
            // planas en Questions con GroupId. Se aplana para que el resto no distinga.
            foreach (var g in q.Groups)
            {
                g.Id = Guid.NewGuid();
                foreach (var p in g.Questions)
                {
                    p.GroupId = g.Id;
                    q.Questions.Add(p);
                }
                g.Questions.Clear();
            }
            return q;
        }

        public static RenderableQuestionnaire Read(string fhirJson)
        {
            if (string.IsNullOrWhiteSpace(fhirJson)) return null;

            FhirQuestionnaire resource;
            try { resource = Parser.Parse<FhirQuestionnaire>(fhirJson); }
            catch (Exception) { return null; }   // recurso ilegible: la pantalla se degrada, no se cae
            if (resource == null) return null;

            var result = new RenderableQuestionnaire
            {
                Title = resource.Title,
                Version = resource.Version
            };

            var sueltas = new List<RenderableQuestion>();
            foreach (var item in resource.Item ?? new List<FhirQuestionnaire.ItemComponent>())
            {
                if (TypeLiteral(item) == "group")
                {
                    var grupo = new RenderableGroup { LinkId = item.LinkId, Title = item.Text };
                    foreach (var hija in item.Item ?? new List<FhirQuestionnaire.ItemComponent>())
                        grupo.Questions.Add(ReadQuestion(hija));
                    if (grupo.Questions.Count > 0) result.Groups.Add(grupo);
                }
                else
                {
                    sueltas.Add(ReadQuestion(item));
                }
            }

            if (sueltas.Count > 0)
            {
                result.Groups.Add(new RenderableGroup
                {
                    Synthetic = true,
                    LinkId = "sin-grupo",
                    Title = result.Title ?? "Preguntas",
                    Questions = sueltas
                });
            }
            ResolveOptionConditions(result);
            return result;
        }

        private static RenderableQuestion ReadQuestion(FhirQuestionnaire.ItemComponent item)
        {
            var question = new RenderableQuestion
            {
                LinkId = item.LinkId,
                Text = item.Text,
                Type = ReadType(item),
                Required = item.Required ?? false,
                MaxLength = item.MaxLength   // campo nativo del item, no extensión
            };

            foreach (var ext in item.Extension ?? new List<Extension>())
            {
                switch (ext.Url)
                {
                    case MinValueUrl: question.Min = Numeric(ext.Value); break;
                    case MaxValueUrl: question.Max = Numeric(ext.Value); break;
                    case UnitUrl: question.Unit = (ext.Value as Coding)?.Code; break;
                    case MimeTypeUrl: question.Accept = (ext.Value as Code)?.Value; break;
                    case RegexUrl: question.Pattern = (ext.Value as FhirString)?.Value; break;
                }
            }

            foreach (var opcion in item.AnswerOption ?? new List<FhirQuestionnaire.AnswerOptionComponent>())
            {
                var option = ReadOption(opcion.Value);
                if (option != null) question.Options.Add(option);
            }

            var condicion = (item.EnableWhen ?? new List<FhirQuestionnaire.EnableWhenComponent>()).FirstOrDefault();
            if (condicion != null)
            {
                question.EnableWhen = new RenderableCondition
                {
                    Question = condicion.Question,
                    Op = OperatorLiteral(condicion) ?? "=",
                    Value = ConditionValue(condicion.Answer)
                };
            }
            return question;
        }

        /// <summary>
        /// Los alias son los textos con los que se guardó la opción antes de guardar el código:
        /// la etiqueta y, en versiones previas a la extensión, el término SNOMED.
        /// </summary>
        private static RenderableOption ReadOption(DataType value)
        {
            string code, display, system = null, term = null;
            switch (value)
            {
                case Coding c:
                    code = c.Code ?? c.Display;
                    display = c.Display ?? c.Code;
                    system = c.System;
                    term = ChequeARExtensions.GetString(c, ChequeARExtensions.OptionSnomedDisplayUrl);
                    break;
                case FhirString s:
                    code = display = s.Value;
                    break;
                default:
                    return null;
            }
            if (string.IsNullOrEmpty(code)) return null;

            var option = new RenderableOption { Code = code, System = system, Display = display };
            foreach (var alias in new[] { display, term })
                if (!string.IsNullOrEmpty(alias) && alias != code && !option.Aliases.Contains(alias))
                    option.Aliases.Add(alias);
            return option;
        }

        /// <summary>
        /// El valor esperado de una condición sobre una opción pasa a ser el código de esa
        /// opción, aunque la versión congelada lo haya guardado con el texto.
        /// </summary>
        private static void ResolveOptionConditions(RenderableQuestionnaire result)
        {
            var questions = result.Groups.SelectMany(g => g.Questions).ToList();
            foreach (var q in questions)
            {
                if (!(q.EnableWhen?.Value is string expected)) continue;
                var source = questions.FirstOrDefault(x => x.LinkId == q.EnableWhen.Question);
                var option = source?.FindOption(expected);
                if (option != null) q.EnableWhen.Value = option.Code;
                // Versiones con la condición numérica guardada como texto: se compara como número.
                else if (source != null && (source.Type == "integer" || source.Type == "decimal" || source.Type == "scale")
                         && decimal.TryParse(expected.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
                    q.EnableWhen.Value = n;
            }
        }

        /// <summary>
        /// Devuelve el vocabulario que hablan las islas Vue, que NO es el de FHIR y no tiene
        /// por qué serlo (ver QuestionTypeMap). Absorbe además el vocabulario R4 de lo ya
        /// congelado.
        /// </summary>
        private static string ReadType(FhirQuestionnaire.ItemComponent item)
        {
            var tipo = TypeLiteral(item) ?? "string";

            // R5: 'coding' cubre la opción única y la selección múltiple; la múltiple es la
            // que repite. Las versiones congeladas antes de corregir el mapeo la declaraban
            // con optionsOrString en lugar de repeats: significan lo mismo para el renderer.
            if (tipo == "coding")
                return item.Repeats == true || ConstraintLiteral(item) == "optionsOrString" ? "openChoice" : "choice";

            // Legado R4 de las versiones congeladas antes de la feature 009.
            if (tipo == "choice") return "choice";
            if (tipo == "open-choice") return "openChoice";

            // FHIR no tiene tipo escala: se modela como integer + itemControl slider.
            if (tipo == "integer" && HasItemControl(item, "slider")) return "scale";

            return tipo;
        }

        /// <summary>
        /// Lee el literal crudo y no la propiedad tipada: con un valor que el enum de R5 no
        /// reconoce (como 'choice'), la propiedad viene en null pero el string original sigue
        /// disponible en el elemento. Es lo que hace legible lo ya publicado.
        /// </summary>
        private static string TypeLiteral(FhirQuestionnaire.ItemComponent item)
            => item?.TypeElement?.ObjectValue as string;

        private static string ConstraintLiteral(FhirQuestionnaire.ItemComponent item)
            => item?.AnswerConstraintElement?.ObjectValue as string;

        private static string OperatorLiteral(FhirQuestionnaire.EnableWhenComponent c)
            => c?.OperatorElement?.ObjectValue as string;

        private static bool HasItemControl(FhirQuestionnaire.ItemComponent item, string code)
        {
            foreach (var ext in item.Extension ?? new List<Extension>())
            {
                if (ext.Url != ItemControlUrl) continue;
                var concept = ext.Value as CodeableConcept;
                if (concept?.Coding?.Any(c => c.Code == code) == true) return true;
            }
            return false;
        }

        private static readonly TimeSpan Argentina = TimeSpan.FromHours(-3);

        /// <summary>
        /// Fecha y hora de una condición como la entrega datetime-local: en hora de Argentina,
        /// sin segundos ni zona. Lo que no tiene hora vuelve igual.
        /// </summary>
        public static string LocalDateTime(string value)
        {
            if (value == null || value.Length <= 16 || value[10] != 'T') return value;
            var conZona = value.EndsWith("Z", StringComparison.Ordinal) || value.LastIndexOf('+') > 10 || value.LastIndexOf('-') > 10;
            if (conZona && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
                return dto.ToOffset(Argentina).ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);
            return value.Substring(0, 16);
        }

        private static decimal? Numeric(DataType value)
        {
            if (value is Integer i) return i.Value;
            if (value is FhirDecimal d) return d.Value;
            return null;
        }

        private static object ConditionValue(DataType answer)
        {
            switch (answer)
            {
                case FhirBoolean b: return b.Value ?? false;
                case Integer i: return (long?)i.Value ?? 0L;
                case FhirDecimal d: return d.Value ?? 0m;
                case Coding c: return c.Code ?? c.Display;
                case FhirString s: return s.Value;
                case Date fecha: return fecha.Value;
                case FhirDateTime momento: return LocalDateTime(momento.Value);
                default: return null;
            }
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
        /// <summary>Agrupa las preguntas sueltas para la pantalla; no existe en el Questionnaire.</summary>
        public bool Synthetic { get; set; }
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
        public int? MaxLength { get; set; }
        public string Pattern { get; set; }
        public string Unit { get; set; }
        public string Accept { get; set; }
        public List<RenderableOption> Options { get; set; } = new List<RenderableOption>();
        public RenderableCondition EnableWhen { get; set; }

        /// <summary>
        /// La opción a la que corresponde un valor guardado: por código o, si no, por un alias
        /// que no sea ambiguo. Null si no hay ninguna, o si el código destino se repite en
        /// otra opción: mapear ahí perdería la distinción entre las dos.
        /// </summary>
        public RenderableOption FindOption(string stored)
        {
            if (string.IsNullOrEmpty(stored) || Options == null) return null;
            var byCode = Options.FirstOrDefault(o => o.Code == stored);
            if (byCode != null) return byCode;
            var byAlias = Options.Where(o => o.Aliases.Contains(stored)).ToList();
            if (byAlias.Count != 1 || Options.Count(o => o.Code == byAlias[0].Code) > 1) return null;
            return byAlias[0];
        }
    }

    public class RenderableOption
    {
        public string Code { get; set; }
        public string System { get; set; }
        public string Display { get; set; }
        public List<string> Aliases { get; set; } = new List<string>();
    }

    public class RenderableCondition
    {
        public string Question { get; set; }
        public string Op { get; set; }
        public object Value { get; set; }
    }
}
