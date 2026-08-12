using System;
using System.Collections.Generic;
using ChequeAR.Application.Validation;
using Xunit;

namespace ChequeAR.Tests.Application
{
    /// <summary>
    /// Evaluación de reglas enableWhen: decide si una pregunta se muestra según la respuesta
    /// de otra. Cubre cada operador y los bordes (regla nula, pregunta sin responder, operador
    /// no soportado, comparación por fallback de string).
    /// </summary>
    public class EnableWhenEvaluatorTests
    {
        private readonly EnableWhenEvaluator _sut = new EnableWhenEvaluator();

        private static Dictionary<string, object> Answers(string linkId, object value)
            => new Dictionary<string, object> { { linkId, value } };

        [Fact]
        public void ReglaNula_SiempreHabilita()
        {
            Assert.True(_sut.IsEnabled(null, new Dictionary<string, object>()));
        }

        [Fact]
        public void PreguntaSinResponder_NoHabilita()
        {
            var rule = new EnableWhenRule { QuestionLinkId = "q1", Operator = "exists" };
            Assert.False(_sut.IsEnabled(rule, new Dictionary<string, object>()));
        }

        [Fact]
        public void Exists_ConValorPresente_Habilita()
        {
            var rule = new EnableWhenRule { QuestionLinkId = "q1", Operator = "exists" };
            Assert.True(_sut.IsEnabled(rule, Answers("q1", "cualquier cosa")));
        }

        [Fact]
        public void Exists_ConValorNulo_NoHabilita()
        {
            var rule = new EnableWhenRule { QuestionLinkId = "q1", Operator = "exists" };
            Assert.False(_sut.IsEnabled(rule, Answers("q1", null)));
        }

        [Theory]
        [InlineData("=", 5, 5, true)]
        [InlineData("=", 5, 6, false)]
        [InlineData("!=", 5, 6, true)]
        [InlineData("!=", 5, 5, false)]
        [InlineData(">", 6, 5, true)]
        [InlineData(">", 5, 5, false)]
        [InlineData("<", 4, 5, true)]
        [InlineData("<", 5, 5, false)]
        [InlineData(">=", 5, 5, true)]
        [InlineData(">=", 4, 5, false)]
        [InlineData("<=", 5, 5, true)]
        [InlineData("<=", 6, 5, false)]
        public void Operadores_ComparanCorrectamente(string op, int actual, int expected, bool enabled)
        {
            var rule = new EnableWhenRule { QuestionLinkId = "q1", Operator = op, Answer = expected };
            Assert.Equal(enabled, _sut.IsEnabled(rule, Answers("q1", actual)));
        }

        [Fact]
        public void OperadorNoSoportado_Lanza()
        {
            var rule = new EnableWhenRule { QuestionLinkId = "q1", Operator = "~=", Answer = 1 };
            Assert.Throws<NotSupportedException>(() => _sut.IsEnabled(rule, Answers("q1", 1)));
        }

        [Fact]
        public void Comparar_CaeAStringCuandoLosValoresNoSonComparables()
        {
            // Dos object planos no implementan IComparable: la comparación cae a string.Compare
            // de sus ToString() ("System.Object" == "System.Object") -> 0, así que ">=" habilita.
            var rule = new EnableWhenRule { QuestionLinkId = "q1", Operator = ">=", Answer = new object() };
            Assert.True(_sut.IsEnabled(rule, Answers("q1", new object())));
        }
    }
}
