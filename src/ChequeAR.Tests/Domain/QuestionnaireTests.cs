using System;
using System.Linq;
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

        // Un cuestionario activo con cambios pendientes se puede volver a publicar: eso
        // congela una versión nueva con los cambios hechos desde la publicación anterior.
        [Fact]
        public void CanPublish_ActiveWithPendingChanges_ReturnsTrue()
        {
            var q = new Questionnaire { Status = QuestionnaireStatus.Active, HasPendingChanges = true };
            q.Questions.Add(new Question());

            Assert.True(q.CanPublish());
        }

        // Un cuestionario activo sin cambios pendientes NO se puede volver a publicar: no hay
        // nada nuevo que congelar respecto de la versión ya publicada.
        [Fact]
        public void CanPublish_ActiveWithoutPendingChanges_ReturnsFalse()
        {
            var q = new Questionnaire { Status = QuestionnaireStatus.Active, HasPendingChanges = false };
            q.Questions.Add(new Question());

            Assert.False(q.CanPublish());
        }

        // Un cuestionario archivado (Retirado) se puede volver a publicar: eso lo saca del
        // archivo y lo deja activo de nuevo con una versión nueva.
        [Fact]
        public void CanPublish_RetiredWithQuestions_ReturnsTrue()
        {
            var q = new Questionnaire { Status = QuestionnaireStatus.Retired };
            q.Questions.Add(new Question());

            Assert.True(q.CanPublish());
        }

        // Publicado y editado después: los cambios quedan pendientes, la versión congelada
        // sigue siendo la que ven los clínicos.
        [Fact]
        public void HasUnpublishedChanges_EditedAfterPublish_ReturnsTrue()
        {
            var q = Published();
            q.HasPendingChanges = true;

            Assert.True(q.HasUnpublishedChanges());
        }

        // Recién publicado no tiene nada pendiente.
        [Fact]
        public void HasUnpublishedChanges_JustPublished_ReturnsFalse()
        {
            var q = Published();

            Assert.False(q.HasUnpublishedChanges());
        }

        // El caso que la heurística vieja (UpdatedAt > PublishedAt) no podía ver: editar
        // dentro del mismo segundo que la publicación. Las columnas son DATE en Oracle,
        // con precisión de 1 segundo, así que los timestamps quedaban iguales.
        [Fact]
        public void HasUnpublishedChanges_EditedSameSecondAsPublish_ReturnsTrue()
        {
            var q = Published();
            q.UpdatedAt = q.Versions.Single().PublishedAt;   // mismo instante, al segundo
            q.HasPendingChanges = true;

            Assert.True(q.HasUnpublishedChanges());
        }

        // Un borrador nunca tiene "cambios sin publicar": todavía no publicó nada contra
        // lo que contrastar, aunque el flag esté prendido por una edición.
        [Fact]
        public void HasUnpublishedChanges_Draft_ReturnsFalse()
        {
            var q = new Questionnaire { Status = QuestionnaireStatus.Draft, HasPendingChanges = true };

            Assert.False(q.HasUnpublishedChanges());
        }

        private static Questionnaire Published()
        {
            var publishedAt = new DateTime(2026, 7, 22, 10, 0, 0, DateTimeKind.Utc);
            var versionId = Guid.NewGuid();
            var q = new Questionnaire
            {
                Status = QuestionnaireStatus.Active,
                CurrentVersionId = versionId,
                UpdatedAt = publishedAt,
                HasPendingChanges = false
            };
            q.Versions.Add(new QuestionnaireVersion { Id = versionId, VersionNumber = 1, PublishedAt = publishedAt });
            return q;
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
