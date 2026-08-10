using System;
using ChequeAR.Application.Fhir;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ChequeAR.Tests.Application
{
    public class FhirQuestionnaireBuilderTests
    {
        // Rango y unidad viajan como extensiones estandar de FHIR, no como un formato
        // propio: la version congelada tiene que ser legible por otro sistema.
        [Fact]
        public void Build_NumericWithRange_EmitsStandardExtensions()
        {
            var q = WithQuestion(new Question
            {
                LinkId = "edad",
                Text = "Edad",
                Type = QuestionType.Integer,
                ValidationJson = "{\"min\":0,\"max\":120,\"unit\":\"anios\"}"
            });

            var item = FirstItem(FhirQuestionnaireBuilder.Build(q, 1));
            var extensions = (JArray)item["extension"];

            Assert.Contains(extensions, e => (string)e["url"] == "http://hl7.org/fhir/StructureDefinition/minValue"
                                          && (int)e["valueInteger"] == 0);
            Assert.Contains(extensions, e => (string)e["url"] == "http://hl7.org/fhir/StructureDefinition/maxValue"
                                          && (int)e["valueInteger"] == 120);
            Assert.Contains(extensions, e => (string)e["url"] == "http://hl7.org/fhir/StructureDefinition/questionnaire-unit"
                                          && (string)e["valueCoding"]["code"] == "anios");
        }

        // El tipo del value[x] acompana al de la pregunta: un decimal no puede publicarse
        // como valueInteger sin perder los decimales.
        [Fact]
        public void Build_DecimalRange_UsesValueDecimal()
        {
            var q = WithQuestion(new Question
            {
                LinkId = "peso",
                Type = QuestionType.Decimal,
                ValidationJson = "{\"min\":0.5}"
            });

            var item = FirstItem(FhirQuestionnaireBuilder.Build(q, 1));
            var min = ((JArray)item["extension"])[0];

            Assert.Equal(0.5m, (decimal)min["valueDecimal"]);
            Assert.Null(min["valueInteger"]);
        }

        // enableWhen: el answer[x] corresponde al tipo de la pregunta REFERENCIADA,
        // no al de la pregunta que lleva la condicion.
        [Fact]
        public void Build_EnableWhen_TypesAnswerFromReferencedQuestion()
        {
            var q = new Questionnaire { Id = Guid.NewGuid(), Title = "T" };
            q.Questions.Add(new Question { LinkId = "hta", Text = "¿HTA?", Type = QuestionType.Boolean, OrderIndex = 0 });
            q.Questions.Add(new Question
            {
                LinkId = "anios_hta",
                Text = "¿Hace cuánto?",
                Type = QuestionType.Integer,      // la pregunta es integer...
                OrderIndex = 1,
                EnableWhenJson = "{\"question\":\"hta\",\"op\":\"=\",\"value\":true}"
            });

            var items = (JArray)JObject.Parse(FhirQuestionnaireBuilder.Build(q, 1))["item"];
            var condition = ((JArray)items[1]["enableWhen"])[0];

            Assert.Equal("hta", (string)condition["question"]);
            Assert.Equal("=", (string)condition["operator"]);
            // ...pero la condicion se evalua contra una booleana
            Assert.True((bool)condition["answerBoolean"]);
            Assert.Equal("all", (string)items[1]["enableBehavior"]);
        }

        // Metadata clinica -> Questionnaire.item.code
        [Fact]
        public void Build_Coding_EmitsItemCode()
        {
            var q = WithQuestion(new Question
            {
                LinkId = "dx",
                Type = QuestionType.String,
                CodingJson = "{\"system\":\"http://snomed.info/sct\",\"code\":\"73211009\",\"display\":\"Diabetes\"}"
            });

            var code = ((JArray)FirstItem(FhirQuestionnaireBuilder.Build(q, 1))["code"])[0];

            Assert.Equal("http://snomed.info/sct", (string)code["system"]);
            Assert.Equal("73211009", (string)code["code"]);
        }

        // El termino clinico es metadata de autoria (la semilla con la que se busco en
        // SNOMED), no parte del concepto publicado: el display del item.code tiene que ser
        // el termino preferido de SNOMED. Si se filtrara, dos cuestionarios con el mismo
        // codigo publicarian displays distintos segun como los hubiera tipeado cada autor.
        [Fact]
        public void Build_CodingWithTerm_PublishesSnomedDisplay_NotAuthoringTerm()
        {
            var q = WithQuestion(new Question
            {
                LinkId = "dx",
                Type = QuestionType.Boolean,
                CodingJson = "{\"system\":\"http://snomed.info/sct\",\"code\":\"29857009\","
                           + "\"display\":\"dolor toracico\",\"term\":\"dolor en el pecho\"}"
            });

            var code = ((JArray)FirstItem(FhirQuestionnaireBuilder.Build(q, 1))["code"])[0];

            Assert.Equal("dolor toracico", (string)code["display"]);
            Assert.Null(code["term"]);
        }

        // Se puede dejar anotado que mide la pregunta antes de haber elegido un concepto.
        // Ese borrador no es codificacion: sin code no hay item.code que emitir, y publicar
        // un coding a medias daria un recurso invalido.
        [Fact]
        public void Build_TermWithoutCode_EmitsNoItemCode()
        {
            var q = WithQuestion(new Question
            {
                LinkId = "dx",
                Type = QuestionType.Boolean,
                CodingJson = "{\"term\":\"dolor toracico\"}"
            });

            Assert.Null(FirstItem(FhirQuestionnaireBuilder.Build(q, 1))["code"]);
        }

        /// <summary>
        /// El término de autoría SÍ viaja, pero como extensión propia y no dentro de item.code.
        /// Hasta la feature 009 no viajaba en absoluto: era metadata que sólo vivía en la base.
        /// Con la interfaz hablando FHIR puro, si no se emite se pierde en CADA guardado —
        /// silenciosamente, porque nada falla: simplemente el campo vuelve vacío.
        /// </summary>
        [Fact]
        public void Build_Term_ViajaComoExtensionPropia()
        {
            var q = WithQuestion(new Question
            {
                LinkId = "dx",
                Type = QuestionType.Boolean,
                CodingJson = "{\"system\":\"http://snomed.info/sct\",\"code\":\"29857009\","
                           + "\"display\":\"dolor toracico\",\"term\":\"dolor en el pecho\"}"
            });

            var item = FirstItem(FhirQuestionnaireBuilder.Build(q, 1));

            Assert.Contains((JArray)item["extension"],
                e => (string)e["url"] == "https://chequear.garrahan/StructureDefinition/question-term"
                  && (string)e["valueString"] == "dolor en el pecho");
        }

        [Fact]
        public void Build_TermSinCodigo_IgualViaja()
        {
            // El caso de quien anota qué mide la pregunta y todavía no eligió concepto.
            var q = WithQuestion(new Question
            {
                LinkId = "dx",
                Type = QuestionType.Boolean,
                CodingJson = "{\"term\":\"dolor toracico\"}"
            });

            var item = FirstItem(FhirQuestionnaireBuilder.Build(q, 1));

            Assert.Null(item["code"]);
            Assert.Contains((JArray)item["extension"],
                e => (string)e["url"] == "https://chequear.garrahan/StructureDefinition/question-term");
        }

        /// <summary>
        /// Las secciones son item de tipo group con las preguntas anidadas adentro. Las
        /// preguntas llegan por Questionnaire.Questions con su GroupId —no por
        /// Group.Questions—, porque el repositorio sólo hace Include de la colección plana.
        /// </summary>
        [Fact]
        public void Build_Secciones_AnidanSusPreguntas()
        {
            var groupId = System.Guid.NewGuid();
            var q = new ChequeAR.Domain.Entities.Questionnaire
            {
                Id = System.Guid.NewGuid(),
                Title = "Ficha",
                SpecialtyCode = "cardio",
                Groups = { new QuestionGroup { Id = groupId, LinkId = "antecedentes", Title = "Antecedentes", OrderIndex = 0 } },
                Questions =
                {
                    new Question { Id = System.Guid.NewGuid(), GroupId = groupId, LinkId = "fuma", Text = "¿Fuma?", Type = QuestionType.Boolean, OrderIndex = 0 },
                    new Question { Id = System.Guid.NewGuid(), LinkId = "suelta", Text = "Suelta", Type = QuestionType.String, OrderIndex = 1 }
                }
            };

            var root = JObject.Parse(FhirQuestionnaireBuilder.Build(q, 1));
            var items = (JArray)root["item"];

            var seccion = items[0];
            Assert.Equal("group", (string)seccion["type"]);
            Assert.Equal("antecedentes", (string)seccion["linkId"]);
            Assert.Equal("fuma", (string)((JArray)seccion["item"])[0]["linkId"]);

            // Una pregunta sin sección queda en la raíz, no se pierde.
            Assert.Equal("suelta", (string)items[1]["linkId"]);
        }

        /// <summary>
        /// Las opciones se emiten como answerOption.valueCoding. Code, Value y Display son
        /// tres campos distintos del dominio y el orden de precedencia importa: es lo que
        /// determina qué guarda la ficha cuando el clínico elige una opción.
        /// </summary>
        [Fact]
        public void Build_Opciones_SalenComoAnswerOptionValueCoding()
        {
            var q = WithQuestion(new Question
            {
                LinkId = "dolor",
                Type = QuestionType.Choice,
                Options =
                {
                    new AnswerOption { Code = "si", Display = "Sí", OrderIndex = 0 },
                    new AnswerOption { Code = "no", Display = "No", OrderIndex = 1 }
                }
            });

            var opciones = (JArray)FirstItem(FhirQuestionnaireBuilder.Build(q, 1))["answerOption"];

            Assert.Equal(2, opciones.Count);
            Assert.Equal("si", (string)opciones[0]["valueCoding"]["code"]);
            Assert.Equal("Sí", (string)opciones[0]["valueCoding"]["display"]);
        }

        // Un JSON corrupto en la base no debe impedir publicar: se ignora esa parte.
        [Fact]
        public void Build_CorruptJson_IsIgnored_AndStillPublishes()
        {
            var q = WithQuestion(new Question
            {
                LinkId = "x",
                Type = QuestionType.Integer,
                ValidationJson = "{no es json",
                EnableWhenJson = "tampoco",
                CodingJson = "{"
            });

            var item = FirstItem(FhirQuestionnaireBuilder.Build(q, 1));

            Assert.Null(item["extension"]);
            Assert.Null(item["enableWhen"]);
            Assert.Null(item["code"]);
            Assert.Equal("x", (string)item["linkId"]);
        }

        // Los tipos que FHIR nombra distinto que el enum del dominio.
        //
        // ⚠️ 'choice' y 'open-choice' eran de R4: R5 los eliminó y ambos son 'coding'.
        // Esta prueba esperaba "open-choice" y pasaba, porque codificaba el mismo error que
        // el builder. El sistema declaraba fhirVersion 5.0.0 sobre vocabulario R4 y el
        // validador de entonces —un sustituto que sólo miraba que existiera "resourceType"—
        // no tenía forma de detectarlo. Ver research.md (R8).
        [Theory]
        [InlineData(QuestionType.DateTime, "dateTime")]
        [InlineData(QuestionType.Choice, "coding")]
        [InlineData(QuestionType.OpenChoice, "coding")]
        [InlineData(QuestionType.Scale, "integer")]
        [InlineData(QuestionType.Text, "text")]
        public void Build_MapsTypeNames(QuestionType type, string expected)
        {
            var q = WithQuestion(new Question { LinkId = "x", Type = type });

            Assert.Equal(expected, (string)FirstItem(FhirQuestionnaireBuilder.Build(q, 1))["type"]);
        }

        /// <summary>
        /// En R5 answerConstraint es lo ÚNICO que separa una opción cerrada de una abierta:
        /// sin él, al releer la versión congelada una pregunta de opción abierta vuelve como
        /// cerrada y el clínico pierde la posibilidad de escribir texto libre.
        /// </summary>
        [Theory]
        [InlineData(QuestionType.Choice, "optionsOnly")]
        [InlineData(QuestionType.OpenChoice, "optionsOrString")]
        public void Build_PreguntasDeOpcion_LlevanAnswerConstraint(QuestionType type, string expected)
        {
            var q = WithQuestion(new Question { LinkId = "x", Type = type });

            Assert.Equal(expected, (string)FirstItem(FhirQuestionnaireBuilder.Build(q, 1))["answerConstraint"]);
        }

        [Fact]
        public void Build_PreguntasQueNoSonDeOpcion_NoLlevanAnswerConstraint()
        {
            var q = WithQuestion(new Question { LinkId = "x", Type = QuestionType.String });

            Assert.Null(FirstItem(FhirQuestionnaireBuilder.Build(q, 1))["answerConstraint"]);
        }

        // Largo máximo de un texto: maxLength es un campo NATIVO del item FHIR, no una
        // extensión. Y el patrón regex viaja como extensión estándar. Round-trip completo:
        // lo que emite el builder lo tiene que volver a leer el reader.
        [Fact]
        public void Build_TextValidation_MaxLengthIsNative_PatternIsExtension()
        {
            var q = WithQuestion(new Question
            {
                LinkId = "dni",
                Text = "DNI",
                Type = QuestionType.String,
                ValidationJson = "{\"maxLength\":8,\"pattern\":\"^[0-9]{7,8}$\"}"
            });

            var json = FhirQuestionnaireBuilder.Build(q, 1);
            var item = FirstItem(json);

            Assert.Equal(8, (int)item["maxLength"]);
            Assert.Contains((JArray)item["extension"],
                e => (string)e["url"] == "http://hl7.org/fhir/StructureDefinition/regex"
                  && (string)e["valueString"] == "^[0-9]{7,8}$");

            // Reader: el ida y vuelta conserva ambos.
            var back = FhirQuestionnaireReader.Read(json).Groups.Count > 0
                ? FhirQuestionnaireReader.Read(json).Groups[0].Questions[0]
                : null;
            Assert.NotNull(back);
            Assert.Equal(8, back.MaxLength);
            Assert.Equal("^[0-9]{7,8}$", back.Pattern);
        }

        private static Questionnaire WithQuestion(Question question)
        {
            var q = new Questionnaire { Id = Guid.NewGuid(), Title = "Test" };
            q.Questions.Add(question);
            return q;
        }

        private static JObject FirstItem(string json) => (JObject)((JArray)JObject.Parse(json)["item"])[0];
    }
}
