using ChequeAR.Application.Services;
using ChequeAR.Domain.Enums;
using Xunit;

namespace ChequeAR.Tests.Application
{
    public class QuestionTypeMapTests
    {
        // Los dos compuestos son los que importan: ToString().ToLowerInvariant() los
        // aplanaba a "openchoice"/"datetime" y el selector de tipo del editor —que compara
        // contra estos strings, igual que el renderer— se quedaba sin opcion seleccionada.
        [Theory]
        [InlineData(QuestionType.OpenChoice, "openChoice")]
        [InlineData(QuestionType.DateTime, "dateTime")]
        [InlineData(QuestionType.String, "string")]
        [InlineData(QuestionType.Scale, "scale")]
        [InlineData(QuestionType.Attachment, "attachment")]
        public void ToUi_UsesIslandVocabulary(QuestionType type, string expected)
        {
            Assert.Equal(expected, QuestionTypeMap.ToUi(type));
        }

        // FromUi es el inverso de ToUi para todos los tipos.
        [Fact]
        public void FromUi_EsInversoDeToUi()
        {
            foreach (QuestionType t in System.Enum.GetValues(typeof(QuestionType)))
                Assert.Equal(t, QuestionTypeMap.FromUi(QuestionTypeMap.ToUi(t)));
        }

        // Un tipo que no existe se rechaza (422), no se convierte en texto en silencio.
        [Fact]
        public void FromUi_TipoDesconocido_Falla()
        {
            Assert.Throws<ChequeAR.Application.Exceptions.ValidationFailedException>(() => QuestionTypeMap.FromUi("cantidad"));
            Assert.False(QuestionTypeMap.TryFromUi("cantidad", out _));
        }

        [Fact]
        public void FromUi_SinTipo_EsTexto()
        {
            Assert.Equal(QuestionType.String, QuestionTypeMap.FromUi(null));
        }
    }
}
