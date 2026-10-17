using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ChequeAR.Domain.Entities;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Enums;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Utility;
using Newtonsoft.Json.Linq;
using DomainQuestionnaire = ChequeAR.Domain.Entities.Questionnaire;
using FhirQuestionnaire = Hl7.Fhir.Model.Questionnaire;

namespace ChequeAR.Application.Fhir
{
    /// <summary>
    /// Traduce un Questionnaire de dominio al recurso FHIR R5.
    ///
    /// Lo consumen dos caminos con semántica distinta:
    ///  - <see cref="Build"/>: la publicación, que congela el JSON en la versión. Lo congelado
    ///    es por definición un recurso activo, fechado en el momento de publicar.
    ///  - <see cref="ToResource"/>: la API, que devuelve el recurso vivo con su estado real
    ///    (un borrador dice draft, un archivado dice retired) y la fecha de su última edición.
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
        private const string MimeTypeUrl = "http://hl7.org/fhir/StructureDefinition/mimeType";
        private const string UnitUrl = "http://hl7.org/fhir/StructureDefinition/questionnaire-unit";
        private const string ItemControlUrl = "http://hl7.org/fhir/StructureDefinition/questionnaire-itemControl";
        private const string ItemControlSystem = "http://hl7.org/fhir/questionnaire-item-control";

        /// <summary>
        /// JSON de la versión que se congela al publicar: siempre status = active y fechada
        /// en <paramref name="publishedAt"/> (UTC; ahora si no se indica).
        /// </summary>
        public static string Build(DomainQuestionnaire q, int versionNumber, DateTime? publishedAt = null)
        {
            var resource = Compose(q, versionNumber, PublicationStatus.Active, publishedAt ?? DateTime.UtcNow);
            return new FhirJsonSerializer().SerializeToString(resource);
        }

        /// <summary>
        /// Si dos recursos congelados dicen lo mismo, ignorando lo que cambia con cada escritura
        /// o publicación (meta.versionId, version y date).
        /// </summary>
        public static bool SameContent(string fhirJsonA, string fhirJsonB)
        {
            if (string.IsNullOrEmpty(fhirJsonA) || string.IsNullOrEmpty(fhirJsonB)) return false;
            var a = JObject.Parse(fhirJsonA);
            var b = JObject.Parse(fhirJsonB);
            foreach (var o in new[] { a, b })
            {
                o.Remove("meta");
                o.Remove("version");
                o.Remove("date");
            }
            return JToken.DeepEquals(a, b);
        }

        /// <summary>
        /// El recurso vivo: estado real del cuestionario y fecha de su última modificación, que
        /// no cambia entre dos lecturas del mismo registro.
        /// </summary>
        public static FhirQuestionnaire ToResource(DomainQuestionnaire q, int versionNumber)
        {
            if (q == null) throw new ArgumentNullException(nameof(q));
            return Compose(q, versionNumber, StatusOf(q.Status), q.UpdatedAt);
        }

        public static PublicationStatus StatusOf(QuestionnaireStatus status)
        {
            switch (status)
            {
                case QuestionnaireStatus.Active: return PublicationStatus.Active;
                case QuestionnaireStatus.Retired: return PublicationStatus.Retired;
                default: return PublicationStatus.Draft;
            }
        }

        private static FhirQuestionnaire Compose(
            DomainQuestionnaire q, int versionNumber, PublicationStatus status, DateTime dateUtc)
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
                    VersionId = q.ConcurrencyVersion.ToString(CultureInfo.InvariantCulture)
                },
                Url = BaseUrl + q.Id,
                Version = versionNumber.ToString(CultureInfo.InvariantCulture),
                Title = q.Title,
                Status = status,
                SubjectType = new ResourceType?[] { ResourceType.Patient },
                Date = DateTime.SpecifyKind(dateUtc, DateTimeKind.Utc)
                               .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
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
            var coding = QuestionMetadataReader.ReadCoding(q.CodingJson);
            if (coding == null) return;

            if (!string.IsNullOrWhiteSpace(coding.Code))
                resource.Code.Add(new Coding(coding.System, coding.Code, coding.Display));

            ChequeARExtensions.SetQuestionnaireTerm(resource, coding.AuthoredTerm);
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

                // Invariante que-1 de R5: un item de tipo group tiene que tener items. Una
                // sección vacía no aporta nada a quien responde y volvería inválido el recurso.
                if (hijas.Count == 0) continue;

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

            var constraint = AnswerConstraintFor(question.Type);
            if (constraint.HasValue) item.AnswerConstraint = constraint.Value;

            if (RepeatsFor(question)) item.Repeats = true;

            AddValidation(item, question);
            AddCoding(item, question);
            AddEnableWhen(item, question, byLinkId);
            // Todo lo demás de la variable viaja denormalizado en campos nativos; la referencia
            // en sí no tiene lugar nativo y sin ella el parser la perdería en cada guardado.
            ChequeARExtensions.SetVariableId(item, question.VariableId);
            ChequeARExtensions.SetVariableVersion(item, question.VariableVersionId.HasValue ? question.VariableRevision : 0);

            foreach (var o in (question.Options ?? new List<AnswerOption>()).OrderBy(x => x.OrderIndex))
                item.AnswerOption.Add(new FhirQuestionnaire.AnswerOptionComponent { Value = OptionCoding(o) });

            return item;
        }

        /// <summary>
        /// El Coding de una opción. Lleva system cuando la opción está codificada en SNOMED
        /// (Code y SnomedCode son el mismo valor: R5 admite un solo Coding por
        /// answerOption.value). Lo usan las opciones y las condiciones que apuntan a ellas,
        /// que en FHIR tienen que coincidir coding contra coding.
        /// El display es la etiqueta de la opción; el término SNOMED va en una extensión.
        /// </summary>
        private static Coding OptionCoding(AnswerOption o)
        {
            var codificada = !string.IsNullOrWhiteSpace(o.SnomedCode);
            var coding = new Coding(
                codificada ? TerminologyService.SnomedSystem : null,
                o.Code ?? o.Value ?? o.Display,
                o.Display ?? o.SnomedDisplay ?? o.Value);
            if (codificada && !string.IsNullOrWhiteSpace(o.SnomedDisplay) && o.SnomedDisplay != coding.Display)
                ChequeARExtensions.SetString(coding, ChequeARExtensions.OptionSnomedDisplayUrl, o.SnomedDisplay);
            return coding;
        }

        /// <summary>
        /// Metadata clínica (SNOMED CT) como item.code, más el término de autoría como
        /// extensión propia. Son cosas distintas: el code es el concepto elegido; el término
        /// es lo que quiso decir quien diseñó la pregunta, y existe aunque no haya código.
        /// </summary>
        private static void AddCoding(FhirQuestionnaire.ItemComponent item, Question question)
        {
            var coding = QuestionMetadataReader.ReadCoding(question.CodingJson);
            if (coding == null) return;

            if (!string.IsNullOrWhiteSpace(coding.Code))
                item.Code.Add(new Coding(coding.System, coding.Code, coding.Display));

            ChequeARExtensions.SetQuestionTerm(item, coding.AuthoredTerm);
        }

        /// <summary>
        /// Lógica condicional. El tipo del answer[x] corresponde a la pregunta REFERENCIADA:
        /// si la condición dice "cuando 'fuma' sea verdadero", el answer es un boolean porque
        /// 'fuma' es boolean, no por el tipo de la pregunta que se está condicionando.
        ///
        /// Una condición incompleta (sin valor) o con un valor que no corresponde al tipo de
        /// la pregunta referenciada no se emite: no hay un answer[x] válido que declarar. La
        /// publicación la rechaza antes (ver QuestionnaireStructureRules), así que esto sólo
        /// afecta al recurso vivo de un borrador en edición.
        /// </summary>
        private static void AddEnableWhen(
            FhirQuestionnaire.ItemComponent item, Question question, IDictionary<string, Question> byLinkId)
        {
            var condition = QuestionMetadataReader.ReadCondition(question.EnableWhenJson);
            if (condition == null) return;

            byLinkId.TryGetValue(condition.Question, out var source);
            var answer = ConditionAnswer(source, condition.Value);
            if (answer == null) return;

            item.EnableWhen.Add(new FhirQuestionnaire.EnableWhenComponent
            {
                Question = condition.Question,
                Operator = ParseOperator(condition.Op),
                Answer = answer
            });
            // Con una sola condición el behavior es irrelevante, pero declararlo deja el
            // recurso listo para cuando se soporte más de una.
            item.EnableBehavior = FhirQuestionnaire.EnableWhenBehavior.All;
        }

        /// <summary>
        /// El answer[x] de una condición sobre <paramref name="source"/>, o null si el valor
        /// falta o no es representable con el tipo de esa pregunta.
        ///
        /// Para preguntas de opción, internamente la condición se expresa con el texto de la
        /// opción (es lo que comparan el renderer y las respuestas guardadas), pero en el
        /// recurso tiene que ir el MISMO Coding que el answerOption —system y código—: es lo
        /// único que un consumidor externo puede comparar.
        /// </summary>
        public static DataType ConditionAnswer(Question source, object value)
        {
            if (value == null) return null;

            switch (source?.Type)
            {
                case QuestionType.Boolean:
                    if (value is bool b) return new FhirBoolean(b);
                    if (value is string s && bool.TryParse(s, out var parsed)) return new FhirBoolean(parsed);
                    return null;

                case QuestionType.Integer:
                case QuestionType.Scale:
                    var n = QuestionMetadataReader.AsDecimal(value);
                    if (!n.HasValue || n.Value != decimal.Truncate(n.Value)
                        || n.Value > int.MaxValue || n.Value < int.MinValue) return null;
                    return new Integer((int)n.Value);

                case QuestionType.Decimal:
                    var d = QuestionMetadataReader.AsDecimal(value);
                    return d.HasValue ? new FhirDecimal(d.Value) : null;

                case QuestionType.Choice:
                case QuestionType.OpenChoice:
                    var opcion = FindOption(source, Convert.ToString(value, CultureInfo.InvariantCulture));
                    return opcion == null ? null : OptionCoding(opcion);

                case QuestionType.Date:
                    var fecha = ParseConditionDate(value);
                    return fecha.HasValue ? new Date(fecha.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) : null;

                // Hora de Argentina, que no tiene horario de verano: R5 exige zona si hay hora.
                case QuestionType.DateTime:
                    var momento = ParseConditionDate(value);
                    return momento.HasValue
                        ? new FhirDateTime(momento.Value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) + "-03:00")
                        : null;

                default:
                    var texto = Convert.ToString(value, CultureInfo.InvariantCulture);
                    return string.IsNullOrEmpty(texto) ? null : new FhirString(texto);
            }
        }

        private static readonly string[] ConditionDateFormats =
            { "yyyy-MM-dd", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "dd/MM/yyyy", "dd/MM/yyyy HH:mm" };

        private static DateTime? ParseConditionDate(object value)
        {
            if (value is DateTime dt) return dt;
            var texto = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
            if (string.IsNullOrEmpty(texto)) return null;
            var local = FhirQuestionnaireReader.LocalDateTime(texto);
            return DateTime.TryParseExact(local, ConditionDateFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed) ? parsed : (DateTime?)null;
        }

        /// <summary>
        /// La opción de <paramref name="source"/> a la que se refiere el valor de una condición.
        /// Se compara contra el texto que muestran el editor y el renderer, y también contra el
        /// código, por si la condición llegó de un recurso externo que la expresa así.
        /// </summary>
        public static AnswerOption FindOption(Question source, string value)
        {
            if (source?.Options == null || string.IsNullOrEmpty(value)) return null;
            return source.Options.FirstOrDefault(o => (o.Display ?? o.Value) == value)
                ?? source.Options.FirstOrDefault(o => o.Code == value || o.SnomedCode == value)
                // condiciones guardadas con el término SNOMED
                ?? source.Options.FirstOrDefault(o => o.SnomedDisplay == value);
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
            var v = QuestionMetadataReader.ReadValidation(question.ValidationJson) ?? new QuestionValidation();
            var esEntero = question.Type == QuestionType.Integer || question.Type == QuestionType.Scale;

            var min = Bound(v.Min, esEntero, esMinimo: true);
            if (min != null) item.Extension.Add(new Extension(MinValueUrl, min));

            var max = Bound(v.Max, esEntero, esMinimo: false);
            if (max != null) item.Extension.Add(new Extension(MaxValueUrl, max));

            // FHIR no tiene tipo "escala": se publica como integer + itemControl slider. Sin
            // esto, al releer la versión congelada una escala vuelve como número común.
            if (question.Type == QuestionType.Scale)
            {
                item.Extension.Add(new Extension(ItemControlUrl,
                    new CodeableConcept(ItemControlSystem, "slider")));
            }

            if (!string.IsNullOrWhiteSpace(v.Accept))
                item.Extension.Add(new Extension(MimeTypeUrl, new Code(v.Accept)));

            if (v.MaxLength.HasValue && v.MaxLength.Value > 0) item.MaxLength = v.MaxLength.Value;


            if (!string.IsNullOrWhiteSpace(v.Unit))
                item.Extension.Add(new Extension(UnitUrl, new Coding("http://unitsofmeasure.org", v.Unit, v.Unit)));
        }

        /// <summary>
        /// El value[x] de minValue/maxValue tiene que ser del tipo de la pregunta. En una
        /// pregunta entera, un límite con decimales se lleva al entero que respeta el rango
        /// original (techo para el mínimo, piso para el máximo): [1,5 – 9,7] admite de 2 a 9.
        /// Un límite que no entra en un entero de FHIR se omite.
        /// </summary>
        private static DataType Bound(decimal? value, bool esEntero, bool esMinimo)
        {
            if (!value.HasValue) return null;
            if (!esEntero) return new FhirDecimal(value.Value);

            var entero = esMinimo ? decimal.Ceiling(value.Value) : decimal.Floor(value.Value);
            if (entero > int.MaxValue || entero < int.MinValue) return null;
            return new Integer((int)entero);
        }

        /// <summary>
        /// ⚠️ R5 ELIMINÓ los tipos 'choice' y 'open-choice' de R4: ambos son 'coding', y lo
        /// que los distingue es la combinación de answerConstraint y repeats (ver
        /// <see cref="AnswerConstraintFor"/> y <see cref="RepeatsFor"/>).
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
        /// Las dos preguntas de opción de ChequeAR sólo admiten opciones del catálogo: ninguna
        /// acepta texto libre, así que ambas son optionsOnly. optionsOrString significaría
        /// "una opción o texto libre", que no es lo que hace la aplicación. Las versiones
        /// congeladas que lo declaran se leen igual como selección múltiple (ver
        /// FhirQuestionnaireReader.ReadType).
        /// </summary>
        public static FhirQuestionnaire.QuestionnaireAnswerConstraint? AnswerConstraintFor(QuestionType type)
        {
            switch (type)
            {
                case QuestionType.Choice:
                case QuestionType.OpenChoice: return FhirQuestionnaire.QuestionnaireAnswerConstraint.OptionsOnly;
                default: return null;
            }
        }

        /// <summary>
        /// OpenChoice es la selección MÚLTIPLE (el renderer muestra casillas y guarda una
        /// respuesta por opción marcada): en FHIR eso es repeats = true. El resto de los tipos
        /// repite sólo si la variable lo declara.
        /// </summary>
        public static bool RepeatsFor(Question question)
            => question.Type == QuestionType.OpenChoice || question.RepeatsAllowed;
    }
}
