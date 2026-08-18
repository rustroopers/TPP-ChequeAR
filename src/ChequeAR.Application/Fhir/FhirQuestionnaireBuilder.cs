using System;
using System.Collections.Generic;
using System.Linq;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using DomainQuestionnaire = ChequeAR.Domain.Entities.Questionnaire;
using FhirQuestionnaire = Hl7.Fhir.Model.Questionnaire;

namespace ChequeAR.Application.Fhir
{
    /// <summary>
    /// Traduce un Questionnaire de dominio al recurso FHIR R5.
    ///
    /// Lo consumen dos caminos: la publicación, que congela el JSON en la versión, y la API,
    /// que devuelve el recurso vivo. Por eso expone las dos formas — POCO y texto.
    ///
    /// La UI publica desde el listado (sin pasar por el editor), así que el recurso tiene que
    /// poder armarse enteramente desde la base.
    /// </summary>
    public static class FhirQuestionnaireBuilder
    {
        public const string ProfileUrl = "https://chequear.garrahan/StructureDefinition/ChequeARQuestionnaire";
        public const string SpecialtySystem = "https://chequear.garrahan/CodeSystem/specialty";
        private const string BaseUrl = "https://chequear.garrahan/Questionnaire/";

        private const string MinValueUrl = "http://hl7.org/fhir/StructureDefinition/minValue";
        private const string MaxValueUrl = "http://hl7.org/fhir/StructureDefinition/maxValue";
        private const string RegexUrl = "http://hl7.org/fhir/StructureDefinition/regex";
        private const string MimeTypeUrl = "http://hl7.org/fhir/StructureDefinition/mimeType";
        private const string UnitUrl = "http://hl7.org/fhir/StructureDefinition/questionnaire-unit";
        private const string ItemControlUrl = "http://hl7.org/fhir/StructureDefinition/questionnaire-itemControl";
        private const string ItemControlSystem = "http://hl7.org/fhir/questionnaire-item-control";

        /// <summary>JSON del recurso, que es lo que se congela en cada versión publicada.</summary>
        public static string Build(DomainQuestionnaire q, int versionNumber)
            => new FhirJsonSerializer().SerializeToString(ToResource(q, versionNumber));

        public static FhirQuestionnaire ToResource(DomainQuestionnaire q, int versionNumber)
        {
            if (q == null) throw new ArgumentNullException(nameof(q));

            var resource = new FhirQuestionnaire
            {
                Id = q.Id.ToString(),
                Meta = new Meta
                {
                    Profile = new[] { ProfileUrl },
                    // La versión del REGISTRO (contador de escrituras para concurrencia
                    // optimista), no el número de versión publicada. Es el mecanismo nativo
                    // del estándar y viaja además en el ETag.
                    VersionId = q.ConcurrencyVersion.ToString()
                },
                Url = BaseUrl + q.Id,
                Version = versionNumber.ToString(),
                Title = q.Title,
                Name = q.Title,
                Status = PublicationStatus.Active,
                SubjectType = new ResourceType?[] { ResourceType.Patient },
                Date = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                Publisher = "Hospital Garrahan"
            };

            if (!string.IsNullOrWhiteSpace(q.Description)) resource.Description = new Markdown(q.Description);
            if (!string.IsNullOrWhiteSpace(q.SpecialtyCode)) resource.UseContext.Add(SpecialtyContext(q));
            AddQuestionnaireCoding(resource, q);

            // Índice linkId -> pregunta: enableWhen tipa su answer[x] según la pregunta
            // REFERENCIADA, no según la que declara la condición. Sin este índice no hay
            // forma de saber qué tipo corresponde.
            var byLinkId = (q.Questions ?? new List<Question>())
                .Where(x => !string.IsNullOrWhiteSpace(x.LinkId))
                .GroupBy(x => x.LinkId)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var item in BuildItems(q, byLinkId)) resource.Item.Add(item);
            return resource;
        }

        /// <summary>
        /// Codificación clínica del cuestionario completo. El concepto SNOMED va en
        /// Questionnaire.code —campo nativo del estándar para "un identificador de este conjunto
        /// de preguntas en una terminología"—; el término de autoría, en la extensión propia. La
        /// simetría con <see cref="AddCoding"/> a nivel pregunta es deliberada: mismo contrato,
        /// mismo par code/term que puede existir por separado.
        /// </summary>
        private static void AddQuestionnaireCoding(FhirQuestionnaire resource, DomainQuestionnaire q)
        {
            var coding = TryParse(q.CodingJson);
            if (coding == null) return;

            var code = (string)coding["code"];
            if (!string.IsNullOrWhiteSpace(code))
                resource.Code.Add(new Coding((string)coding["system"], code, (string)coding["display"]));

            ChequeARExtensions.SetQuestionnaireTerm(resource, (string)coding["term"]);
        }

        private static UsageContext SpecialtyContext(DomainQuestionnaire q)
        {
            return new UsageContext
            {
                Code = new Coding("http://terminology.hl7.org/CodeSystem/usage-context-type", "focus"),
                Value = new CodeableConcept(SpecialtySystem, q.SpecialtyCode, q.SpecialtyDisplay ?? q.SpecialtyCode, null)
            };
        }

        private static List<FhirQuestionnaire.ItemComponent> BuildItems(
            DomainQuestionnaire q, IDictionary<string, Question> byLinkId)
        {
            var items = new List<FhirQuestionnaire.ItemComponent>();
            var agrupadas = new HashSet<Guid>();

            foreach (var g in (q.Groups ?? new List<QuestionGroup>()).OrderBy(x => x.OrderIndex))
            {
                // Las preguntas llegan por Questionnaire.Questions (con GroupId), no por
                // Group.Questions: el repositorio sólo hace Include de la colección plana.
                var hijas = (q.Questions ?? new List<Question>())
                    .Where(x => x.GroupId == g.Id)
                    .OrderBy(x => x.OrderIndex)
                    .ToList();
                foreach (var c in hijas) agrupadas.Add(c.Id);

                var grupo = new FhirQuestionnaire.ItemComponent
                {
                    LinkId = string.IsNullOrWhiteSpace(g.LinkId) ? g.Id.ToString() : g.LinkId,
                    Text = g.Title,
                    Type = FhirQuestionnaire.QuestionnaireItemType.Group
                };
                foreach (var h in hijas) grupo.Item.Add(BuildQuestion(h, byLinkId));
                items.Add(grupo);
            }

            var sueltas = (q.Questions ?? new List<Question>())
                .Where(x => !agrupadas.Contains(x.Id))
                .OrderBy(x => x.OrderIndex);
            foreach (var s in sueltas) items.Add(BuildQuestion(s, byLinkId));

            return items;
        }

        private static FhirQuestionnaire.ItemComponent BuildQuestion(
            Question question, IDictionary<string, Question> byLinkId)
        {
            var item = new FhirQuestionnaire.ItemComponent
            {
                LinkId = string.IsNullOrWhiteSpace(question.LinkId) ? question.Id.ToString() : question.LinkId,
                Text = question.Text,
                Type = FhirType(question.Type),
                Required = question.Required
            };

            // Sólo las preguntas de opción llevan answerConstraint. Es lo ÚNICO que distingue
            // una opción cerrada de una abierta en R5, donde ambas son type = coding.
            var constraint = AnswerConstraintFor(question.Type);
            if (constraint.HasValue) item.AnswerConstraint = constraint.Value;

            if (question.RepeatsAllowed) item.Repeats = true;

            AddValidation(item, question);
            AddCoding(item, question);
            AddEnableWhen(item, question, byLinkId);

            foreach (var o in (question.Options ?? new List<AnswerOption>()).OrderBy(x => x.OrderIndex))
            {
                item.AnswerOption.Add(new FhirQuestionnaire.AnswerOptionComponent
                {
                    Value = new Coding(null, o.Code ?? o.Value ?? o.Display, o.Display ?? o.Value)
                });
            }

            return item;
        }

        /// <summary>
        /// Metadata clínica (SNOMED CT) como item.code, más el término de autoría como
        /// extensión propia. Son cosas distintas: el code es el concepto elegido; el término
        /// es lo que quiso decir quien diseñó la pregunta, y existe aunque no haya código.
        /// </summary>
        private static void AddCoding(FhirQuestionnaire.ItemComponent item, Question question)
        {
            var coding = TryParse(question.CodingJson);
            if (coding == null) return;

            var code = (string)coding["code"];
            if (!string.IsNullOrWhiteSpace(code))
                item.Code.Add(new Coding((string)coding["system"], code, (string)coding["display"]));

            ChequeARExtensions.SetQuestionTerm(item, (string)coding["term"]);
        }

        /// <summary>
        /// Lógica condicional. El tipo del answer[x] corresponde a la pregunta REFERENCIADA:
        /// si la condición dice "cuando 'fuma' sea verdadero", el answer es un boolean porque
        /// 'fuma' es boolean, no por el tipo de la pregunta que se está condicionando.
        /// </summary>
        private static void AddEnableWhen(
            FhirQuestionnaire.ItemComponent item, Question question, IDictionary<string, Question> byLinkId)
        {
            var condition = TryParse(question.EnableWhenJson);
            if (condition == null) return;

            var sourceLinkId = (string)condition["question"];
            if (string.IsNullOrWhiteSpace(sourceLinkId)) return;

            Question source;
            byLinkId.TryGetValue(sourceLinkId, out source);

            item.EnableWhen.Add(new FhirQuestionnaire.EnableWhenComponent
            {
                Question = sourceLinkId,
                Operator = ParseOperator((string)condition["op"]),
                Answer = AnswerFor(source?.Type, condition["value"])
            });
            // Con una sola condición el behavior es irrelevante, pero declararlo deja el
            // recurso listo para cuando se soporte más de una.
            item.EnableBehavior = FhirQuestionnaire.EnableWhenBehavior.All;
        }

        private static DataType AnswerFor(QuestionType? sourceType, JToken value)
        {
            switch (sourceType)
            {
                case QuestionType.Boolean:
                    return new FhirBoolean((bool?)value ?? false);
                case QuestionType.Integer:
                case QuestionType.Scale:
                    return new Integer((int?)(long?)value ?? 0);
                case QuestionType.Decimal:
                    return new FhirDecimal((decimal?)value ?? 0m);
                case QuestionType.Choice:
                case QuestionType.OpenChoice:
                    var code = (string)value;
                    return new Coding(null, code, code);
                default:
                    return new FhirString((string)value);
            }
        }

        private static FhirQuestionnaire.QuestionnaireItemOperator ParseOperator(string op)
        {
            if (string.IsNullOrWhiteSpace(op)) return FhirQuestionnaire.QuestionnaireItemOperator.Equal;
            var parsed = EnumUtility.ParseLiteral<FhirQuestionnaire.QuestionnaireItemOperator>(op);
            return parsed ?? FhirQuestionnaire.QuestionnaireItemOperator.Equal;
        }

        /// <summary>
        /// Rango, unidad, patrón y formatos aceptados como extensiones ESTÁNDAR, en vez de un
        /// formato propio: así la versión congelada sigue siendo interpretable por otro
        /// sistema. maxLength es campo nativo del item, no extensión.
        /// </summary>
        private static void AddValidation(FhirQuestionnaire.ItemComponent item, Question question)
        {
            // Un ValidationJson ausente o corrupto se publica sin restricciones, pero el
            // marcador de escala se emite igual: hace al TIPO, no a la validación.
            var v = TryParse(question.ValidationJson) ?? new JObject();
            var esEntero = question.Type == QuestionType.Integer || question.Type == QuestionType.Scale;

            var min = v["min"];
            if (min != null && min.Type != JTokenType.Null)
                item.Extension.Add(new Extension(MinValueUrl, Numeric(min, esEntero)));

            var max = v["max"];
            if (max != null && max.Type != JTokenType.Null)
                item.Extension.Add(new Extension(MaxValueUrl, Numeric(max, esEntero)));

            // FHIR no tiene tipo "escala": se publica como integer + itemControl slider. Sin
            // esto, al releer la versión congelada una escala vuelve como número común.
            if (question.Type == QuestionType.Scale)
            {
                item.Extension.Add(new Extension(ItemControlUrl,
                    new CodeableConcept(ItemControlSystem, "slider")));
            }

            var accept = (string)v["accept"];
            if (!string.IsNullOrWhiteSpace(accept))
                item.Extension.Add(new Extension(MimeTypeUrl, new Code(accept)));

            var maxLength = v["maxLength"];
            if (maxLength != null && maxLength.Type != JTokenType.Null)
            {
                var n = (long)maxLength;
                if (n > 0) item.MaxLength = (int)n;
            }

            var pattern = (string)v["pattern"];
            if (!string.IsNullOrWhiteSpace(pattern))
                item.Extension.Add(new Extension(RegexUrl, new FhirString(pattern)));

            var unit = (string)v["unit"];
            if (!string.IsNullOrWhiteSpace(unit))
                item.Extension.Add(new Extension(UnitUrl, new Coding("http://unitsofmeasure.org", unit, unit)));
        }

        // El tipo del value[x] tiene que acompañar al de la pregunta.
        private static DataType Numeric(JToken value, bool esEntero)
            => esEntero ? (DataType)new Integer((int)(long)value) : new FhirDecimal((decimal)value);

        private static JObject TryParse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JObject.Parse(json); }
            catch (JsonException) { return null; }
        }

        /// <summary>
        /// ⚠️ R5 ELIMINÓ los tipos 'choice' y 'open-choice' de R4: ambos son 'coding', y lo
        /// que los distingue es answerConstraint (ver AnswerConstraintFor). El builder
        /// artesanal seguía emitiendo el vocabulario viejo mientras declaraba fhirVersion
        /// 5.0.0, y nadie lo notó porque el validador era un sustituto que no validaba nada.
        /// </summary>
        public static FhirQuestionnaire.QuestionnaireItemType FhirType(QuestionType type)
        {
            switch (type)
            {
                case QuestionType.Decimal:    return FhirQuestionnaire.QuestionnaireItemType.Decimal;
                case QuestionType.Integer:    return FhirQuestionnaire.QuestionnaireItemType.Integer;
                case QuestionType.String:     return FhirQuestionnaire.QuestionnaireItemType.String;
                case QuestionType.Text:       return FhirQuestionnaire.QuestionnaireItemType.Text;
                case QuestionType.Date:       return FhirQuestionnaire.QuestionnaireItemType.Date;
                case QuestionType.DateTime:   return FhirQuestionnaire.QuestionnaireItemType.DateTime;
                case QuestionType.Boolean:    return FhirQuestionnaire.QuestionnaireItemType.Boolean;
                case QuestionType.Attachment: return FhirQuestionnaire.QuestionnaireItemType.Attachment;
                case QuestionType.Choice:
                case QuestionType.OpenChoice: return FhirQuestionnaire.QuestionnaireItemType.Coding;
                case QuestionType.Scale:      return FhirQuestionnaire.QuestionnaireItemType.Integer;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, "Tipo de pregunta sin mapeo a FHIR R5.");
            }
        }

        /// <summary>
        /// Lo único que separa una opción cerrada de una abierta en R5. Sin esto, al releer
        /// la versión congelada una pregunta de opción abierta vuelve como cerrada.
        /// </summary>
        public static FhirQuestionnaire.QuestionnaireAnswerConstraint? AnswerConstraintFor(QuestionType type)
        {
            switch (type)
            {
                case QuestionType.Choice:     return FhirQuestionnaire.QuestionnaireAnswerConstraint.OptionsOnly;
                case QuestionType.OpenChoice: return FhirQuestionnaire.QuestionnaireAnswerConstraint.OptionsOrString;
                default: return null;
            }
        }
    }
}
