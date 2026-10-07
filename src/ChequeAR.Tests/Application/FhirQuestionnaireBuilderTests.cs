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
        /// Ninguna pregunta de opción de ChequeAR acepta texto libre, así que ambas son
        /// optionsOnly. Lo que distingue a OpenChoice —selección múltiple, con casillas en el
        /// renderer— es repeats. optionsOrString significaría "una opción o texto libre", que
        /// declararía en el recurso un comportamiento que la aplicación no tiene.
        /// </summary>
        [Theory]
        [InlineData(QuestionType.Choice, false)]
        [InlineData(QuestionType.OpenChoice, true)]
        public void Build_PreguntasDeOpcion_SonOptionsOnly_YLaMultipleRepite(QuestionType type, bool repite)
        {
            var q = WithQuestion(new Question { LinkId = "x", Type = type });

            var item = FirstItem(FhirQuestionnaireBuilder.Build(q, 1));

            Assert.Equal("optionsOnly", (string)item["answerConstraint"]);
            Assert.Equal(repite, item["repeats"] != null && (bool)item["repeats"]);
        }

        // La versión congelada se relee con la misma semántica: coding + repeats es la múltiple.
        [Theory]
        [InlineData(QuestionType.Choice, "choice")]
        [InlineData(QuestionType.OpenChoice, "openChoice")]
        public void Build_PreguntasDeOpcion_VuelvenConSuTipoAlReleer(QuestionType type, string esperado)
        {
            var json = FhirQuestionnaireBuilder.Build(WithQuestion(new Question { LinkId = "x", Type = type }), 1);

            Assert.Equal(esperado, FhirQuestionnaireReader.Read(json).Groups[0].Questions[0].Type);
        }

        /// <summary>
        /// En FHIR una condición sobre una pregunta de opción se cumple comparando Coding
        /// contra Coding. Si el answerCoding no lleva el mismo system y código que el
        /// answerOption, un consumidor externo nunca habilita la pregunta dependiente.
        /// </summary>
        [Fact]
        public void Build_EnableWhenSobreOpcion_UsaElCodingDeLaOpcion()
        {
            var q = new Questionnaire { Id = Guid.NewGuid(), Title = "T" };
            q.Questions.Add(new Question
            {
                Id = Guid.NewGuid(), LinkId = "fuma", Type = QuestionType.Choice, OrderIndex = 0,
                Options =
                {
                    new AnswerOption { Code = "77176002", SnomedCode = "77176002", Display = "Fumador", SnomedDisplay = "Fumador", OrderIndex = 0 },
                    new AnswerOption { Code = "8392000", SnomedCode = "8392000", Display = "No fumador", SnomedDisplay = "No fumador", OrderIndex = 1 }
                }
            });
            q.Questions.Add(new Question
            {
                Id = Guid.NewGuid(), LinkId = "cuanto", Type = QuestionType.Integer, OrderIndex = 1,
                // Internamente la condición se guarda con el texto de la opción.
                EnableWhenJson = "{\"question\":\"fuma\",\"op\":\"=\",\"value\":\"Fumador\"}"
            });

            var items = (JArray)JObject.Parse(FhirQuestionnaireBuilder.Build(q, 1))["item"];
            var opcion = items[0]["answerOption"][0]["valueCoding"];
            var respuesta = items[1]["enableWhen"][0]["answerCoding"];

            Assert.Equal("http://snomed.info/sct", (string)respuesta["system"]);
            Assert.Equal((string)opcion["system"], (string)respuesta["system"]);
            Assert.Equal((string)opcion["code"], (string)respuesta["code"]);
        }

        // Una condición a medio armar (se eligió la pregunta, todavía no el valor) no tiene un
        // answer[x] válido: no se inventa un "false" ni un "0" que cambiaría su sentido.
        [Fact]
        public void Build_EnableWhenSinValor_NoSeEmite()
        {
            var q = new Questionnaire { Id = Guid.NewGuid(), Title = "T" };
            q.Questions.Add(new Question { Id = Guid.NewGuid(), LinkId = "fuma", Type = QuestionType.Boolean, OrderIndex = 0 });
            q.Questions.Add(new Question
            {
                Id = Guid.NewGuid(), LinkId = "cuanto", Type = QuestionType.Integer, OrderIndex = 1,
                EnableWhenJson = "{\"question\":\"fuma\",\"op\":\"=\",\"value\":\"\"}"
            });

            var items = (JArray)JObject.Parse(FhirQuestionnaireBuilder.Build(q, 1))["item"];

            Assert.Null(items[1]["enableWhen"]);
        }

        // El recurso vivo dice el estado real: un borrador no es "active". Lo congelado sí.
        [Theory]
        [InlineData(QuestionnaireStatus.Draft, "draft")]
        [InlineData(QuestionnaireStatus.Active, "active")]
        [InlineData(QuestionnaireStatus.Retired, "retired")]
        public void ToResource_DeclaraElEstadoReal(QuestionnaireStatus estado, string esperado)
        {
            var q = WithQuestion(new Question { LinkId = "x", Type = QuestionType.String });
            q.Status = estado;

            var recurso = FhirQuestionnaireBuilder.ToResource(q, 0);

            Assert.Equal(esperado, recurso.StatusElement.ObjectValue as string);
            Assert.Equal("active", (string)JObject.Parse(FhirQuestionnaireBuilder.Build(q, 1))["status"]);
        }

        // Dos lecturas del mismo registro devuelven la misma fecha: la de su última edición.
        [Fact]
        public void ToResource_FechaEsLaDeLaUltimaEdicion()
        {
            var q = WithQuestion(new Question { LinkId = "x", Type = QuestionType.String });
            q.UpdatedAt = new DateTime(2026, 5, 4, 13, 20, 0, DateTimeKind.Utc);

            Assert.Equal("2026-05-04T13:20:00Z", FhirQuestionnaireBuilder.ToResource(q, 0).Date);
        }

        // Invariante que-1 de R5: un group sin items vuelve inválido el recurso.
        [Fact]
        public void Build_SeccionVacia_NoSeEmite()
        {
            var vacia = Guid.NewGuid();
            var q = new Questionnaire { Id = Guid.NewGuid(), Title = "T" };
            q.Groups.Add(new QuestionGroup { Id = vacia, LinkId = "vacia", Title = "Vacía" });
            q.Questions.Add(new Question { Id = Guid.NewGuid(), LinkId = "x", Type = QuestionType.String });

            var items = (JArray)JObject.Parse(FhirQuestionnaireBuilder.Build(q, 1))["item"];

            Assert.Single(items);
            Assert.Equal("x", (string)items[0]["linkId"]);
        }

        // Un valor con el tipo equivocado en el JSON guardado no tumba la publicación.
        [Fact]
        public void Build_ValidacionConTiposInesperados_NoTira()
        {
            var q = WithQuestion(new Question
            {
                LinkId = "x",
                Type = QuestionType.Integer,
                ValidationJson = "{\"min\":\"abc\",\"max\":{\"a\":1},\"maxLength\":\"mucho\"}"
            });

            var item = FirstItem(FhirQuestionnaireBuilder.Build(q, 1));

            Assert.Null(item["extension"]);
            Assert.Null(item["maxLength"]);
        }

        // En una pregunta entera, [1,5 – 9,7] admite de 2 a 9: no se redondea a ciegas.
        [Fact]
        public void Build_RangoConDecimalesEnPreguntaEntera_RespetaElRangoOriginal()
        {
            var q = WithQuestion(new Question
            {
                LinkId = "x",
                Type = QuestionType.Integer,
                ValidationJson = "{\"min\":1.5,\"max\":9.7}"
            });

            var extensiones = (JArray)FirstItem(FhirQuestionnaireBuilder.Build(q, 1))["extension"];

            Assert.Contains(extensiones, e => ((string)e["url"]).EndsWith("minValue") && (int)e["valueInteger"] == 2);
            Assert.Contains(extensiones, e => ((string)e["url"]).EndsWith("maxValue") && (int)e["valueInteger"] == 9);
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
