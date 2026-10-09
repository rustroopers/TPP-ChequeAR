using System.Collections.Generic;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using Xunit;

namespace ChequeAR.Tests.Application
{
    /// <summary>
    /// Lo que vuelve inservible una versión congelada: condiciones que no se pueden cumplir
    /// (círculos, operadores que el renderer no sabe evaluar) y preguntas que no se pueden
    /// responder. Son reglas puras sobre la entidad, sin repositorios.
    /// </summary>
    public class QuestionnaireStructureRulesTests
    {
        private static Question Pregunta(string linkId, QuestionType type = QuestionType.Integer,
            string dependeDe = null, string op = "=", string valor = "1", params string[] opciones)
        {
            var q = new Question { LinkId = linkId, Text = linkId, Type = type };
            if (dependeDe != null)
                q.EnableWhenJson = "{\"question\":\"" + dependeDe + "\",\"op\":\"" + op + "\",\"value\":" + valor + "}";
            for (var i = 0; i < opciones.Length; i++)
                q.Options.Add(new AnswerOption { Value = opciones[i], Display = opciones[i], Code = opciones[i], OrderIndex = i });
            return q;
        }

        private static IReadOnlyList<string> Publicar(params Question[] preguntas)
            => QuestionnaireStructureRules.CheckForPublish(new List<QuestionGroup>(), preguntas);

        // ── Círculos ─────────────────────────────────────────────────────────────────

        [Fact]
        public void CondicionesEnCirculo_NoSePublican()
        {
            var errores = Publicar(
                Pregunta("a", dependeDe: "b"),
                Pregunta("b", dependeDe: "a"));

            var circulo = Assert.Single(errores, e => e.Contains("círculo"));
            Assert.Contains("'a'", circulo);
            Assert.Contains("'b'", circulo);
        }

        [Fact]
        public void CirculoDeTres_SeReportaUnaSolaVez()
        {
            var errores = Publicar(
                Pregunta("a", dependeDe: "c"),
                Pregunta("b", dependeDe: "a"),
                Pregunta("c", dependeDe: "b"));

            Assert.Single(errores, e => e.Contains("círculo"));
        }

        // Una cadena sin vuelta atrás es legítima: c se ve si b se ve, y b si a se respondió.
        [Fact]
        public void CadenaSinCirculo_SePublica()
        {
            var errores = Publicar(
                Pregunta("a"),
                Pregunta("b", dependeDe: "a"),
                Pregunta("c", dependeDe: "b"));

            Assert.Empty(errores);
        }

        // ── Operadores ───────────────────────────────────────────────────────────────

        [Fact]
        public void MayorQueSobreUnaOpcion_NoSePublica()
        {
            var errores = Publicar(
                Pregunta("sev", QuestionType.Choice, opciones: new[] { "Leve", "Grave" }),
                Pregunta("x", dependeDe: "sev", op: ">", valor: "\"Leve\""));

            Assert.Contains(errores, e => e.Contains("no es numérica"));
        }

        [Fact]
        public void MenorQueSobreUnBooleano_NoSePublica()
        {
            var errores = Publicar(
                Pregunta("fuma", QuestionType.Boolean),
                Pregunta("x", dependeDe: "fuma", op: "<", valor: "true"));

            Assert.Contains(errores, e => e.Contains("no es numérica"));
        }

        [Theory]
        [InlineData(QuestionType.Integer)]
        [InlineData(QuestionType.Decimal)]
        [InlineData(QuestionType.Scale)]
        public void MayorQueSobreUnNumero_SePublica(QuestionType tipo)
        {
            var errores = Publicar(
                Pregunta("fc", tipo),
                Pregunta("x", dependeDe: "fc", op: ">", valor: "100"));

            Assert.Empty(errores);
        }

        [Fact]
        public void OperadorDesconocido_NoSePublica()
        {
            var errores = Publicar(
                Pregunta("fc"),
                Pregunta("x", dependeDe: "fc", op: "exists", valor: "true"));

            Assert.Contains(errores, e => e.Contains("'exists'"));
        }

        // ── Preguntas de opción ──────────────────────────────────────────────────────

        [Theory]
        [InlineData(QuestionType.Choice)]
        [InlineData(QuestionType.OpenChoice)]
        public void PreguntaDeOpcionSinOpciones_NoSePublica(QuestionType tipo)
        {
            var errores = Publicar(Pregunta("sev", tipo));

            Assert.Contains(errores, e => e.Contains("no tiene opciones"));
        }

        [Fact]
        public void PreguntaDeOpcionConOpciones_SePublica()
        {
            Assert.Empty(Publicar(Pregunta("sev", QuestionType.Choice, opciones: new[] { "Leve" })));
        }

        // Sigue siendo un problema sólo al publicar: un borrador puede estar a medio armar.
        [Fact]
        public void PreguntaDeOpcionSinOpciones_SeGuardaComoBorrador()
        {
            var sinOpciones = new[] { Pregunta("sev", QuestionType.Choice) };
            Assert.Empty(QuestionnaireStructureRules.CheckForSave(new List<QuestionGroup>(), sinOpciones));
        }
    }
}
