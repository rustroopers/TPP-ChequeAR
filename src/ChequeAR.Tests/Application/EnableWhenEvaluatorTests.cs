using System;
using System.Collections.Generic;
using System.Linq;
using ChequeAR.Application.Fhir;
using ChequeAR.Application.Validation;
using ChequeAR.Domain.Entities;
using Xunit;

namespace ChequeAR.Tests.Application
{
    /// <summary>
    /// Visibilidad (enableWhen) y completitud del lado del servidor. Cada caso replica un
    /// comportamiento del renderer: si divergieran, el servidor rechazaría fichas que la
    /// pantalla mostró completas (o aceptaría las que mostró incompletas).
    /// </summary>
    public class EnableWhenEvaluatorTests
    {
        // ---- Compare: compareAnswer del renderer ----

        [Theory]
        [InlineData("=", 5L, 5L, true)]
        [InlineData("=", 5L, 6L, false)]
        [InlineData("!=", 5L, 6L, true)]
        [InlineData("!=", 5L, 5L, false)]
        [InlineData(">", 6L, 5L, true)]
        [InlineData(">", 5L, 5L, false)]
        [InlineData("<", 4L, 5L, true)]
        [InlineData("<", 5L, 5L, false)]
        public void Operadores_ComparanCorrectamente(string op, long actual, long expected, bool visible)
        {
            Assert.Equal(visible, EnableWhenEvaluator.Compare(op, actual, expected));
        }

        [Fact]
        public void Igual_NumeroEsperadoContraTextoConComaDecimal()
        {
            // Los inputs de texto devuelven "5,5" contra un valor esperado tipado.
            Assert.True(EnableWhenEvaluator.Compare("=", "5,5", 5.5m));
        }

        [Fact]
        public void SinRespuesta_IgualYComparacionesNoSeCumplen_DistintoSi()
        {
            Assert.False(EnableWhenEvaluator.Compare("=", null, 5L));
            Assert.False(EnableWhenEvaluator.Compare(">", null, 5L));
            Assert.False(EnableWhenEvaluator.Compare("<", null, 5L));
            Assert.True(EnableWhenEvaluator.Compare("!=", null, 5L));
        }

        [Fact]
        public void OperadorDesconocido_NoOcultaLaPregunta()
        {
            Assert.True(EnableWhenEvaluator.Compare("exists", null, null));
        }

        [Fact]
        public void Booleano_ComparaPorValor()
        {
            Assert.True(EnableWhenEvaluator.Compare("=", true, true));
            Assert.False(EnableWhenEvaluator.Compare("=", false, true));
        }

        [Fact]
        public void Texto_ComparacionExacta()
        {
            Assert.True(EnableWhenEvaluator.Compare("=", "Sí", "Sí"));
            Assert.False(EnableWhenEvaluator.Compare("=", "sí", "Sí"));
        }

        // ---- MissingRequired ----

        [Fact]
        public void ObligatoriaVisibleSinResponder_Falta()
        {
            var s = Estructura(Pregunta("edad", required: true));

            var faltan = EnableWhenEvaluator.MissingRequired(s, new List<QuestionnaireResponseItem>());

            Assert.Equal(new[] { "edad" }, faltan.Select(q => q.LinkId));
        }

        [Fact]
        public void ObligatoriaRespondida_NoFalta()
        {
            var s = Estructura(Pregunta("edad", required: true));

            Assert.Empty(EnableWhenEvaluator.MissingRequired(s, new[] { new QuestionnaireResponseItem { LinkId = "edad", ValueInteger = 7 } }));
        }

        [Fact]
        public void ItemSinValor_CuentaComoSinResponder()
        {
            // El renderer manda un item con sólo LinkId para las preguntas vacías.
            var s = Estructura(Pregunta("obs", required: true));

            Assert.Single(EnableWhenEvaluator.MissingRequired(s, new[] { new QuestionnaireResponseItem { LinkId = "obs" } }));
        }

        [Fact]
        public void BooleanoEnFalso_CuentaComoRespondido()
        {
            var s = Estructura(Pregunta("fiebre", required: true, type: "boolean"));

            Assert.Empty(EnableWhenEvaluator.MissingRequired(s, new[] { new QuestionnaireResponseItem { LinkId = "fiebre", ValueBoolean = false } }));
        }

        [Fact]
        public void AdjuntoSubido_CuentaComoRespondido()
        {
            var s = Estructura(Pregunta("estudio", required: true, type: "attachment"));

            Assert.Empty(EnableWhenEvaluator.MissingRequired(s, new[] { new QuestionnaireResponseItem { LinkId = "estudio", AttachmentId = Guid.NewGuid() } }));
        }

        [Fact]
        public void ObligatoriaOculta_NoSeExige()
        {
            var s = Estructura(
                Pregunta("fiebre", type: "boolean"),
                Pregunta("temp", required: true, cond: new RenderableCondition { Question = "fiebre", Op = "=", Value = true }));

            var items = new[] { new QuestionnaireResponseItem { LinkId = "fiebre", ValueBoolean = false } };

            Assert.Empty(EnableWhenEvaluator.MissingRequired(s, items));
        }

        [Fact]
        public void ObligatoriaQueSeVuelveVisible_SeExige()
        {
            var s = Estructura(
                Pregunta("fiebre", type: "boolean"),
                Pregunta("temp", required: true, cond: new RenderableCondition { Question = "fiebre", Op = "=", Value = true }));

            var items = new[] { new QuestionnaireResponseItem { LinkId = "fiebre", ValueBoolean = true } };

            Assert.Equal(new[] { "temp" }, EnableWhenEvaluator.MissingRequired(s, items).Select(q => q.LinkId));
        }

        [Fact]
        public void OrigenMultiple_AlcanzaConUnaOpcionQueCumpla()
        {
            var s = Estructura(
                Pregunta("sintomas", type: "openChoice"),
                Pregunta("detalle", required: true, cond: new RenderableCondition { Question = "sintomas", Op = "=", Value = "Tos" }));

            var items = new[]
            {
                new QuestionnaireResponseItem { LinkId = "sintomas", ValueCode = "Fiebre" },
                new QuestionnaireResponseItem { LinkId = "sintomas", ValueCode = "Tos" }
            };

            Assert.Single(EnableWhenEvaluator.MissingRequired(s, items));
        }

        [Fact]
        public void OrigenMultipleSinMarcar_DistintoSeCumple()
        {
            // Igual que una pregunta de respuesta única sin responder: ninguna respuesta es igual.
            var s = Estructura(
                Pregunta("sintomas", type: "openChoice"),
                Pregunta("detalle", required: true, cond: new RenderableCondition { Question = "sintomas", Op = "!=", Value = "Ninguno" }));

            Assert.Single(EnableWhenEvaluator.MissingRequired(s, new List<QuestionnaireResponseItem>()));
        }

        [Fact]
        public void OrigenMultiple_DistintoNoSeCumpleSiAlgunaEsIgual()
        {
            var s = Estructura(
                Pregunta("sintomas", type: "openChoice"),
                Pregunta("detalle", required: true, cond: new RenderableCondition { Question = "sintomas", Op = "!=", Value = "Tos" }));
            var items = new[]
            {
                new QuestionnaireResponseItem { LinkId = "sintomas", ValueCode = "Fiebre" },
                new QuestionnaireResponseItem { LinkId = "sintomas", ValueCode = "Tos" }
            };

            Assert.Empty(EnableWhenEvaluator.MissingRequired(s, items));
        }

        [Fact]
        public void OrigenFecha_ComparaComoElRenderer()
        {
            var s = Estructura(
                Pregunta("inicio", type: "date"),
                Pregunta("detalle", type: "string", cond: new RenderableCondition { Question = "inicio", Op = "=", Value = "2024-05-01" }));
            var detalle = new QuestionnaireResponseItem { LinkId = "detalle", ValueString = "x" };
            var items = new List<QuestionnaireResponseItem>
            {
                new QuestionnaireResponseItem { LinkId = "inicio", ValueDate = new DateTime(2024, 5, 1) }, detalle
            };

            Assert.Empty(EnableWhenEvaluator.ClearHiddenAnswers(s, items));
            Assert.Equal("x", detalle.ValueString);
        }

        [Fact]
        public void Cadena_SiLaOrigenQuedaOculta_LaDependienteTambien()
        {
            var s = Estructura(
                Pregunta("a", type: "boolean"),
                Pregunta("b", type: "boolean", cond: new RenderableCondition { Question = "a", Op = "=", Value = true }),
                Pregunta("c", type: "string", cond: new RenderableCondition { Question = "b", Op = "=", Value = true }));
            var b = new QuestionnaireResponseItem { LinkId = "b", ValueBoolean = true };
            var c = new QuestionnaireResponseItem { LinkId = "c", ValueString = "x" };

            var vaciadas = EnableWhenEvaluator.ClearHiddenAnswers(s, new List<QuestionnaireResponseItem>
            {
                new QuestionnaireResponseItem { LinkId = "a", ValueBoolean = false }, b, c
            });

            Assert.Equal(new[] { "b", "c" }, vaciadas.OrderBy(x => x));
            Assert.Null(c.ValueString);
        }

        [Fact]
        public void Cadena_ObligatoriaConOrigenOcultaYDistinto_SeExige()
        {
            // c se ve cuando b no es "x"; con b oculta, b no tiene respuesta y c aparece.
            var s = Estructura(
                Pregunta("a", type: "boolean"),
                Pregunta("b", type: "string", cond: new RenderableCondition { Question = "a", Op = "=", Value = true }),
                Pregunta("c", required: true, cond: new RenderableCondition { Question = "b", Op = "!=", Value = "x" }));
            var items = new[]
            {
                new QuestionnaireResponseItem { LinkId = "a", ValueBoolean = false },
                new QuestionnaireResponseItem { LinkId = "b", ValueString = "x" }
            };

            Assert.Equal(new[] { "c" }, EnableWhenEvaluator.MissingRequired(s, items).Select(q => q.LinkId));
        }

        // ---- ClearHiddenAnswers ----

        [Fact]
        public void ClearHiddenAnswers_VaciaLaRespuestaDeUnaPreguntaQueQuedoOculta()
        {
            var s = Estructura(
                Pregunta("fc", type: "integer"),
                Pregunta("ta", type: "integer", cond: new RenderableCondition { Question = "fc", Op = ">", Value = 5L }));
            var ta = new QuestionnaireResponseItem { LinkId = "ta", ValueInteger = 23 };
            var items = new List<QuestionnaireResponseItem> { new QuestionnaireResponseItem { LinkId = "fc", ValueInteger = 0 }, ta };

            var vaciadas = EnableWhenEvaluator.ClearHiddenAnswers(s, items);

            Assert.Equal(new[] { "ta" }, vaciadas);
            Assert.Null(ta.ValueInteger);
            Assert.Equal(0, items[0].ValueInteger);
        }

        [Fact]
        public void ClearHiddenAnswers_NoTocaLasVisibles()
        {
            var s = Estructura(
                Pregunta("fc", type: "integer"),
                Pregunta("ta", type: "integer", cond: new RenderableCondition { Question = "fc", Op = ">", Value = 5L }));
            var ta = new QuestionnaireResponseItem { LinkId = "ta", ValueInteger = 23 };

            var vaciadas = EnableWhenEvaluator.ClearHiddenAnswers(s,
                new List<QuestionnaireResponseItem> { new QuestionnaireResponseItem { LinkId = "fc", ValueInteger = 80 }, ta });

            Assert.Empty(vaciadas);
            Assert.Equal(23, ta.ValueInteger);
        }

        [Fact]
        public void ClearHiddenAnswers_VaciaTodosLosValoresYElAdjunto()
        {
            var s = Estructura(
                Pregunta("fiebre", type: "boolean"),
                Pregunta("estudio", type: "attachment", cond: new RenderableCondition { Question = "fiebre", Op = "=", Value = true }));
            var estudio = new QuestionnaireResponseItem { LinkId = "estudio", AttachmentId = Guid.NewGuid(), ValueString = "rx.pdf" };

            EnableWhenEvaluator.ClearHiddenAnswers(s,
                new List<QuestionnaireResponseItem> { new QuestionnaireResponseItem { LinkId = "fiebre", ValueBoolean = false }, estudio });

            Assert.Null(estudio.AttachmentId);
            Assert.Null(estudio.ValueString);
        }

        [Fact]
        public void ClearUnknownAnswers_VaciaLoQueNoEstaEnLaVersion()
        {
            var s = Estructura(Pregunta("edad", type: "integer"));
            var vieja = new QuestionnaireResponseItem { LinkId = "retirada", ValueString = "x" };
            var edad = new QuestionnaireResponseItem { LinkId = "edad", ValueInteger = 3 };

            var vaciadas = EnableWhenEvaluator.ClearUnknownAnswers(s, new List<QuestionnaireResponseItem> { vieja, edad });

            Assert.Equal(new[] { "retirada" }, vaciadas);
            Assert.Null(vieja.ValueString);
            Assert.Equal(3, edad.ValueInteger);
        }

        [Fact]
        public void SinEstructura_NoSeValida()
        {
            Assert.Empty(EnableWhenEvaluator.MissingRequired(null, new List<QuestionnaireResponseItem>()));
        }

        private static RenderableQuestionnaire Estructura(params RenderableQuestion[] preguntas)
        {
            var s = new RenderableQuestionnaire();
            s.Groups.Add(new RenderableGroup { LinkId = "g1", Title = "General", Questions = preguntas.ToList() });
            return s;
        }

        private static RenderableQuestion Pregunta(string linkId, bool required = false, string type = "string", RenderableCondition cond = null)
            => new RenderableQuestion { LinkId = linkId, Text = linkId, Type = type, Required = required, EnableWhen = cond };
    }
}
