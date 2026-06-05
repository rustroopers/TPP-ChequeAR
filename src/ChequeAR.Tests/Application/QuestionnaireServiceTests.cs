using System;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using Moq;
using Xunit;

namespace ChequeAR.Tests.Application
{
    public class QuestionnaireServiceTests
    {
        private readonly Mock<IQuestionnaireRepository> _repo = new Mock<IQuestionnaireRepository>();
        private readonly Mock<IFhirValidator> _validator = new Mock<IFhirValidator>();
        private readonly QuestionnaireService _sut;

        public QuestionnaireServiceTests()
        {
            _sut = new QuestionnaireService(_repo.Object, _validator.Object, TestHelpers.NoopAuditDispatcher());
        }

        // Al crear: arranca en estado Draft, se le asigna Id y datos de auditoría (creador/fecha)
        // y se guarda en el repositorio.
        [Fact]
        public async Task CreateAsync_SetsDraftAndAuditFields_AndPersists()
        {
            var q = new Questionnaire { Title = "Reumato" };

            var created = await _sut.CreateAsync(q, "user-1");

            Assert.NotEqual(Guid.Empty, created.Id);
            Assert.Equal(QuestionnaireStatus.Draft, created.Status);
            Assert.Equal("user-1", created.CreatedBy);
            Assert.NotEqual(default, created.CreatedAt);
            _repo.Verify(r => r.AddAsync(It.IsAny<Questionnaire>()), Times.Once);
        }

        // Editar un cuestionario que no existe debe fallar (no se puede actualizar la nada).
        [Fact]
        public async Task UpdateAsync_NotFound_Throws()
        {
            _repo.Setup(r => r.GetAsync(It.IsAny<Guid>())).ReturnsAsync((Questionnaire)null);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _sut.UpdateAsync(Guid.NewGuid(), new Questionnaire(), "u"));
        }

        // Un cuestionario Retirado (archivado) ya no se puede editar.
        [Fact]
        public async Task UpdateAsync_Retired_Throws()
        {
            var id = Guid.NewGuid();
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(new Questionnaire { Id = id, Status = QuestionnaireStatus.Retired });

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _sut.UpdateAsync(id, new Questionnaire { Title = "x" }, "u"));
        }

        // Al editar, solo se pisan los campos que vienen con valor en el patch; los que vienen
        // null conservan el valor original (actualización parcial).
        [Fact]
        public async Task UpdateAsync_MergesNonNullFields_KeepsExistingWhenNull()
        {
            var id = Guid.NewGuid();
            var existing = new Questionnaire { Id = id, Title = "old", Description = "desc", SpecialtyCode = "REU", Status = QuestionnaireStatus.Active };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(existing);

            var updated = await _sut.UpdateAsync(id, new Questionnaire { Title = "new" }, "u");

            Assert.Equal("new", updated.Title);
            Assert.Equal("desc", updated.Description);   // unchanged because patch was null
            Assert.Equal("REU", updated.SpecialtyCode);
            _repo.Verify(r => r.UpdateAsync(existing), Times.Once);
        }

        // Publicar un cuestionario inexistente debe fallar.
        [Fact]
        public async Task PublishAsync_NotFound_Throws()
        {
            _repo.Setup(r => r.GetAsync(It.IsAny<Guid>())).ReturnsAsync((Questionnaire)null);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _sut.PublishAsync(Guid.NewGuid(), "{}", "u"));
        }

        // No se puede publicar si el cuestionario no está en un estado publicable
        // (acá: Activo y sin preguntas -> CanPublish() es false).
        [Fact]
        public async Task PublishAsync_CannotPublish_Throws()
        {
            var id = Guid.NewGuid();
            // Active + no questions => CanPublish() false
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(new Questionnaire { Id = id, Status = QuestionnaireStatus.Active });

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _sut.PublishAsync(id, "{}", "u"));
        }

        // Si el JSON FHIR no pasa la validación, publicar falla y NO se crea ninguna versión
        // (no se persiste nada inconsistente).
        [Fact]
        public async Task PublishAsync_FhirInvalid_Throws_AndDoesNotPersist()
        {
            var id = Guid.NewGuid();
            var q = Draftable(id);
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(q);
            _validator.Setup(v => v.Validate(It.IsAny<string>(), It.IsAny<string>()))
                      .Returns(TestHelpers.ValidFail("missing title"));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _sut.PublishAsync(id, "{}", "u"));

            _repo.Verify(r => r.AddVersionAsync(It.IsAny<QuestionnaireVersion>()), Times.Never);
        }

        // Publicación exitosa: el cuestionario pasa a Activo, se crea la versión 1 y se apunta
        // CurrentVersionId a esa versión recién creada.
        [Fact]
        public async Task PublishAsync_Valid_CreatesVersionAndActivates()
        {
            var id = Guid.NewGuid();
            var q = Draftable(id);
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(q);
            _validator.Setup(v => v.Validate(It.IsAny<string>(), It.IsAny<string>()))
                      .Returns(TestHelpers.ValidOk());

            var result = await _sut.PublishAsync(id, "{\"resourceType\":\"Questionnaire\"}", "u");

            Assert.Equal(QuestionnaireStatus.Active, result.Status);
            Assert.NotNull(result.CurrentVersionId);
            _repo.Verify(r => r.AddVersionAsync(It.Is<QuestionnaireVersion>(v => v.VersionNumber == 1)), Times.Once);
            _repo.Verify(r => r.UpdateAsync(q), Times.Once);
        }

        // Archivar deja el cuestionario en estado Retirado y guarda el cambio.
        [Fact]
        public async Task ArchiveAsync_SetsRetired()
        {
            var id = Guid.NewGuid();
            var q = new Questionnaire { Id = id, Status = QuestionnaireStatus.Active };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(q);

            await _sut.ArchiveAsync(id, "u");

            Assert.Equal(QuestionnaireStatus.Retired, q.Status);
            _repo.Verify(r => r.UpdateAsync(q), Times.Once);
        }

        private static Questionnaire Draftable(Guid id)
        {
            var q = new Questionnaire { Id = id, Status = QuestionnaireStatus.Draft };
            q.Questions.Add(new Question());
            return q;
        }
    }
}
