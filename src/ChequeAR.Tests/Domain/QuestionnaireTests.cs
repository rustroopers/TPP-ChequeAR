using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using Xunit;

namespace ChequeAR.Tests.Domain
{
    public class QuestionnaireTests
    {
        // Un cuestionario en borrador que tiene al menos una pregunta SÍ se puede publicar.
        [Fact]
        public void CanPublish_DraftWithQuestions_ReturnsTrue()
        {
            var q = new Questionnaire { Status = QuestionnaireStatus.Draft };
            q.Questions.Add(new Question());

            Assert.True(q.CanPublish());
        }

        // Un borrador sin preguntas NO se puede publicar (no tendría contenido que responder).
        [Fact]
        public void CanPublish_DraftWithoutQuestions_ReturnsFalse()
        {
            var q = new Questionnaire { Status = QuestionnaireStatus.Draft };

            Assert.False(q.CanPublish());
        }

        // Solo los borradores se publican: si ya está Activo o Retirado, NO se puede volver a publicar.
        [Theory]
        [InlineData(QuestionnaireStatus.Active)]
        [InlineData(QuestionnaireStatus.Retired)]
        public void CanPublish_NonDraftStatus_ReturnsFalse(QuestionnaireStatus status)
        {
            var q = new Questionnaire { Status = status };
            q.Questions.Add(new Question());

            Assert.False(q.CanPublish());
        }

        // Un cuestionario recién creado ya trae sus listas inicializadas (no null), para poder
        // agregar versiones/grupos/preguntas sin riesgo de NullReferenceException.
        [Fact]
        public void NewQuestionnaire_HasInitializedCollections()
        {
            var q = new Questionnaire();

            Assert.NotNull(q.Versions);
            Assert.NotNull(q.Groups);
            Assert.NotNull(q.Questions);
            Assert.Empty(q.Questions);
        }
    }
}
