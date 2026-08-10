using System;
using System.Collections.Generic;
using System.Linq;
using ChequeAR.Application.Fhir;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ChequeAR.Tests.Application
{
    /// <summary>
    /// Ida y vuelta dominio → FHIR → dominio.
    ///
    /// Es la red contra el riesgo más serio de la feature: con una sola representación, cada
    /// guardado pasa por este par. Si el builder emite algo que el parser no reconstruye, el
    /// dato desaparece en el próximo guardado **sin que nada falle** — el campo simplemente
    /// vuelve vacío y quien diseñó el cuestionario lo descubre semanas después.
    ///
    /// Estas pruebas se escribieron ANTES de tocar el JavaScript del editor, a propósito.
    /// </summary>
    public class FhirRoundTripTests
    {
        [Theory]
        [InlineData(QuestionType.Decimal)]
        [InlineData(QuestionType.Integer)]
        [InlineData(QuestionType.String)]
        [InlineData(QuestionType.Text)]
        [InlineData(QuestionType.Date)]
        [InlineData(QuestionType.DateTime)]
        [InlineData(QuestionType.Boolean)]
        [InlineData(QuestionType.Choice)]
        [InlineData(QuestionType.OpenChoice)]
        [InlineData(QuestionType.Attachment)]
        [InlineData(QuestionType.Scale)]
        public void LosOnceTipos_SobrevivenElCiclo(QuestionType tipo)
        {
            var original = ConUnaPregunta(new Question { LinkId = "x", Text = "Pregunta", Type = tipo });

            Assert.Equal(tipo, Ciclo(original).Questions.Single().Type);
        }

        /// <summary>
        /// El término de autoría es el campo con más riesgo: no tiene lugar nativo en FHIR y
        /// hasta esta feature no viajaba. Si la extensión se rompe, se pierde en silencio.
        /// </summary>
        [Fact]
        public void TerminoDeAutoria_SobreviveElCiclo()
        {
            var original = ConUnaPregunta(new Question
            {
                LinkId = "dx",
                Type = QuestionType.Boolean,
                CodingJson = "{\"system\":\"http://snomed.info/sct\",\"code\":\"29857009\","
                           + "\"display\":\"dolor torácico\",\"term\":\"dolor en el pecho\"}"
            });

            var coding = JObject.Parse(Ciclo(original).Questions.Single().CodingJson);

            Assert.Equal("dolor en el pecho", (string)coding["term"]);
            Assert.Equal("29857009", (string)coding["code"]);
            Assert.Equal("http://snomed.info/sct", (string)coding["system"]);
            Assert.Equal("dolor torácico", (string)coding["display"]);
        }

        [Fact]
        public void TerminoSinCodigo_SobreviveIgual()
        {
            var original = ConUnaPregunta(new Question
            {
                LinkId = "dx",
                Type = QuestionType.Boolean,
                CodingJson = "{\"term\":\"algo que todavía no codifiqué\"}"
            });

            var coding = JObject.Parse(Ciclo(original).Questions.Single().CodingJson);

            Assert.Equal("algo que todavía no codifiqué", (string)coding["term"]);
            Assert.Null(coding["code"]);
        }

        [Fact]
        public void Opciones_ConservanCodigoYEtiqueta()
        {
            var original = ConUnaPregunta(new Question
            {
                LinkId = "dolor",
                Type = QuestionType.Choice,
                Options =
                {
                    new AnswerOption { Code = "leve", Display = "Leve", Value = "Leve", OrderIndex = 0 },
                    new AnswerOption { Code = "severo", Display = "Severo", Value = "Severo", OrderIndex = 1 }
                }
            });

            var opciones = Ciclo(original).Questions.Single().Options.OrderBy(o => o.OrderIndex).ToList();

            Assert.Equal(2, opciones.Count);
            Assert.Equal("leve", opciones[0].Code);
            Assert.Equal("Leve", opciones[0].Display);
            Assert.Equal("severo", opciones[1].Code);
        }

        [Theory]
        [InlineData(QuestionType.Integer, "{\"min\":0,\"max\":10}")]
        [InlineData(QuestionType.Decimal, "{\"min\":0.5,\"max\":9.5}")]
        public void RangoNumerico_SobreviveElCiclo(QuestionType tipo, string validacion)
        {
            var original = ConUnaPregunta(new Question { LinkId = "n", Type = tipo, ValidationJson = validacion });

            var v = JObject.Parse(Ciclo(original).Questions.Single().ValidationJson);

            Assert.Equal(JObject.Parse(validacion)["min"], v["min"]);
            Assert.Equal(JObject.Parse(validacion)["max"], v["max"]);
        }

        [Fact]
        public void UnidadDeMedida_SobreviveElCiclo()
        {
            var original = ConUnaPregunta(new Question
            {
                LinkId = "peso", Type = QuestionType.Decimal,
                ValidationJson = "{\"min\":0,\"max\":200,\"unit\":\"kg\"}"
            });

            Assert.Equal("kg", (string)JObject.Parse(Ciclo(original).Questions.Single().ValidationJson)["unit"]);
        }

        [Fact]
        public void LargoMaximoYPatron_SobrevivenElCiclo()
        {
            var original = ConUnaPregunta(new Question
            {
                LinkId = "dni", Type = QuestionType.String,
                ValidationJson = "{\"maxLength\":8,\"pattern\":\"^[0-9]{7,8}$\"}"
            });

            var v = JObject.Parse(Ciclo(original).Questions.Single().ValidationJson);

            Assert.Equal(8, (int)v["maxLength"]);
            Assert.Equal("^[0-9]{7,8}$", (string)v["pattern"]);
        }

        [Fact]
        public void FormatosAceptadosDeAdjunto_SobrevivenElCiclo()
        {
            var original = ConUnaPregunta(new Question
            {
                LinkId = "rx", Type = QuestionType.Attachment,
                ValidationJson = "{\"accept\":\"image/jpeg,application/pdf\"}"
            });

            Assert.Equal("image/jpeg,application/pdf",
                (string)JObject.Parse(Ciclo(original).Questions.Single().ValidationJson)["accept"]);
        }

        /// <summary>
        /// La escala no existe en FHIR: viaja como integer + itemControl slider. Sin el
        /// marcador, vuelve como número común y el clínico ve un input en vez de un deslizador.
        /// </summary>
        [Fact]
        public void Escala_NoDegradaANumeroComun()
        {
            var original = ConUnaPregunta(new Question
            {
                LinkId = "dolor", Type = QuestionType.Scale,
                ValidationJson = "{\"min\":0,\"max\":10}"
            });

            var vuelta = Ciclo(original).Questions.Single();

            Assert.Equal(QuestionType.Scale, vuelta.Type);
            Assert.Equal(10, (int)JObject.Parse(vuelta.ValidationJson)["max"]);
        }

        [Fact]
        public void Obligatoriedad_YRepeticion_SobrevivenElCiclo()
        {
            var original = ConUnaPregunta(new Question
            {
                LinkId = "x", Type = QuestionType.String, Required = true, RepeatsAllowed = true
            });

            var vuelta = Ciclo(original).Questions.Single();

            Assert.True(vuelta.Required);
            Assert.True(vuelta.RepeatsAllowed);
        }

        /// <summary>
        /// La condición referencia otra pregunta y su valor se tipa según AQUÉLLA. Un error
        /// acá produce una pregunta que nunca se muestra, y sólo se descubre respondiendo.
        /// </summary>
        [Fact]
        public void CondicionSobreBooleano_SobreviveElCiclo()
        {
            var original = new ChequeAR.Domain.Entities.Questionnaire
            {
                Id = Guid.NewGuid(), Title = "Ficha", SpecialtyCode = "cardio",
                Questions =
                {
                    new Question { Id = Guid.NewGuid(), LinkId = "fuma", Text = "¿Fuma?", Type = QuestionType.Boolean, OrderIndex = 0 },
                    new Question
                    {
                        Id = Guid.NewGuid(), LinkId = "cuantos", Text = "¿Cuántos por día?", Type = QuestionType.Integer, OrderIndex = 1,
                        EnableWhenJson = "{\"question\":\"fuma\",\"op\":\"=\",\"value\":true}"
                    }
                }
            };

            var condicional = Ciclo(original).Questions.Single(x => x.LinkId == "cuantos");
            var cond = JObject.Parse(condicional.EnableWhenJson);

            Assert.Equal("fuma", (string)cond["question"]);
            Assert.Equal("=", (string)cond["op"]);
            Assert.True((bool)cond["value"]);
        }

        [Fact]
        public void SeccionesAnidadas_ConservanJerarquiaYOrden()
        {
            var g1 = Guid.NewGuid();
            var g2 = Guid.NewGuid();
            var original = new ChequeAR.Domain.Entities.Questionnaire
            {
                Id = Guid.NewGuid(), Title = "Ficha", SpecialtyCode = "cardio",
                Groups =
                {
                    new QuestionGroup { Id = g1, LinkId = "antecedentes", Title = "Antecedentes", OrderIndex = 0 },
                    new QuestionGroup { Id = g2, LinkId = "examen", Title = "Examen físico", OrderIndex = 1 }
                },
                Questions =
                {
                    new Question { Id = Guid.NewGuid(), GroupId = g1, LinkId = "fuma", Type = QuestionType.Boolean, OrderIndex = 0 },
                    new Question { Id = Guid.NewGuid(), GroupId = g2, LinkId = "ta", Type = QuestionType.String, OrderIndex = 1 },
                    new Question { Id = Guid.NewGuid(), GroupId = g2, LinkId = "fc", Type = QuestionType.Integer, OrderIndex = 2 }
                }
            };

            var vuelta = Ciclo(original);
            var grupos = vuelta.Groups.OrderBy(g => g.OrderIndex).ToList();

            Assert.Equal(2, grupos.Count);
            Assert.Equal("Antecedentes", grupos[0].Title);
            Assert.Equal(new[] { "fuma" }, grupos[0].Questions.Select(x => x.LinkId));
            Assert.Equal(new[] { "ta", "fc" }, grupos[1].Questions.OrderBy(x => x.OrderIndex).Select(x => x.LinkId));
        }

        [Fact]
        public void TituloYEspecialidad_SobrevivenElCiclo()
        {
            var original = new ChequeAR.Domain.Entities.Questionnaire
            {
                Id = Guid.NewGuid(),
                Title = "Control cardiológico",
                Description = "Seguimiento post-quirúrgico",
                SpecialtyCode = "cardio",
                SpecialtyDisplay = "Cardiología",
                Questions = { new Question { Id = Guid.NewGuid(), LinkId = "x", Type = QuestionType.String } }
            };

            var vuelta = Ciclo(original);

            Assert.Equal("Control cardiológico", vuelta.Title);
            Assert.Equal("Seguimiento post-quirúrgico", vuelta.Description);
            Assert.Equal("cardio", vuelta.SpecialtyCode);
            Assert.Equal("Cardiología", vuelta.SpecialtyDisplay);
        }

        [Fact]
        public void PreguntaSinRestricciones_NoInventaValidationJson()
        {
            // Guardar "{}" ensucia la base y hace que un campo vacío parezca configurado.
            var original = ConUnaPregunta(new Question { LinkId = "x", Type = QuestionType.String });

            var vuelta = Ciclo(original).Questions.Single();

            Assert.Null(vuelta.ValidationJson);
            Assert.Null(vuelta.EnableWhenJson);
            Assert.Null(vuelta.CodingJson);
        }

        // ---- helpers ----

        private static ChequeAR.Domain.Entities.Questionnaire ConUnaPregunta(Question question)
        {
            question.Id = Guid.NewGuid();
            return new ChequeAR.Domain.Entities.Questionnaire
            {
                Id = Guid.NewGuid(),
                Title = "Ficha",
                SpecialtyCode = "cardio",
                Questions = { question }
            };
        }

        /// <summary>Dominio → recurso → dominio, pasando por la serialización real.</summary>
        private static ChequeAR.Domain.Entities.Questionnaire Ciclo(ChequeAR.Domain.Entities.Questionnaire q)
        {
            var json = FhirQuestionnaireBuilder.Build(q, 1);
            var recurso = FhirResourceReader.ReadQuestionnaire(json);
            Assert.NotNull(recurso);
            return FhirQuestionnaireParser.ToDomain(recurso);
        }
    }
}
