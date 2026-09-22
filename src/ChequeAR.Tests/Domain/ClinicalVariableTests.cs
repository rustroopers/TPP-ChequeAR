using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using Xunit;

namespace ChequeAR.Tests.Domain
{
    public class ClinicalVariableTests
    {
        // Qué tipos admiten unidad y rango es una regla del dominio, no del formulario: la usan
        // tanto la validación del servicio como el ABM para decidir qué campos mostrar, y
        // tenerla en un solo lugar evita que las dos respuestas se separen.
        [Theory]
        [InlineData(QuestionType.Decimal, true)]
        [InlineData(QuestionType.Integer, true)]
        [InlineData(QuestionType.Scale, true)]
        [InlineData(QuestionType.String, false)]
        [InlineData(QuestionType.Boolean, false)]
        [InlineData(QuestionType.Choice, false)]
        public void IsNumeric_SegunTipo(QuestionType type, bool esperado)
            => Assert.Equal(esperado, new ClinicalVariable { Type = type }.IsNumeric());

        // Una variable de opción sin opciones no se puede responder.
        [Theory]
        [InlineData(QuestionType.Choice, true)]
        [InlineData(QuestionType.OpenChoice, true)]
        [InlineData(QuestionType.Decimal, false)]
        [InlineData(QuestionType.Text, false)]
        public void RequiresOptions_SegunTipo(QuestionType type, bool esperado)
            => Assert.Equal(esperado, new ClinicalVariable { Type = type }.RequiresOptions());

        // MaxLength sobre un número o una fecha no significa nada; guardarlo dejaría un residuo
        // que viajaría al ValidationJson de cada pregunta que use la variable.
        [Theory]
        [InlineData(QuestionType.String, true)]
        [InlineData(QuestionType.Text, true)]
        [InlineData(QuestionType.Integer, false)]
        [InlineData(QuestionType.Date, false)]
        public void AcceptsMaxLength_SegunTipo(QuestionType type, bool esperado)
            => Assert.Equal(esperado, new ClinicalVariable { Type = type }.AcceptsMaxLength());
    }
}
