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
        [Theory]
        [InlineData(QuestionType.DateTime, "dateTime")]
        [InlineData(QuestionType.OpenChoice, "open-choice")]
        [InlineData(QuestionType.Scale, "integer")]
        [InlineData(QuestionType.Text, "text")]
        public void Build_MapsTypeNames(QuestionType type, string expected)
        {
            var q = WithQuestion(new Question { LinkId = "x", Type = type });

            Assert.Equal(expected, (string)FirstItem(FhirQuestionnaireBuilder.Build(q, 1))["type"]);
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
