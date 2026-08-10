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
    }
}
