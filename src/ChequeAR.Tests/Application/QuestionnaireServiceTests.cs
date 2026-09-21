using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Exceptions;
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
        private readonly Mock<IQuestionnaireResponseRepository> _responses = new Mock<IQuestionnaireResponseRepository>();
        private readonly Mock<IFhirValidator> _validator = new Mock<IFhirValidator>();
        private readonly QuestionnaireService _sut;

        public QuestionnaireServiceTests()
        {
            _sut = new QuestionnaireService(_repo.Object, _responses.Object, _validator.Object, TestHelpers.NoopAuditDispatcher(),
                TestHelpers.UnrestrictedGuard(), TestHelpers.CatalogWith(), TestHelpers.UnrestrictedAccess());
        }

        // Al crear: arranca en estado Draft, se le asigna Id y datos de auditoría (creador/fecha)
        // y se guarda en el repositorio.
        [Fact]
        public async Task CreateAsync_SetsDraftAndAuditFields_AndPersists()
        {
            var q = new Questionnaire { Title = "Reumato", SpecialtyCode = "cardio" };

            var created = await _sut.CreateAsync(q, "user-1");

            Assert.NotEqual(Guid.Empty, created.Id);
            Assert.Equal(QuestionnaireStatus.Draft, created.Status);
            Assert.Equal("user-1", created.CreatedBy);
            Assert.NotEqual(default, created.CreatedAt);
            _repo.Verify(r => r.AddAsync(It.IsAny<Questionnaire>()), Times.Once);
        }

        // Candidatos para "importar preguntas": misma especialidad, excluye archivados y el propio.
        [Fact]
        public async Task GetImportCandidates_ExcluyeArchivadosYElPropio()
        {
            var self = Guid.NewGuid();
            var draft = new Questionnaire { Id = Guid.NewGuid(), Title = "Borrador", Status = QuestionnaireStatus.Draft, SpecialtyCode = "reuma" };
            var active = new Questionnaire { Id = Guid.NewGuid(), Title = "Publicado", Status = QuestionnaireStatus.Active, SpecialtyCode = "reuma" };
            var retired = new Questionnaire { Id = Guid.NewGuid(), Title = "Archivado", Status = QuestionnaireStatus.Retired, SpecialtyCode = "reuma" };
            var itself = new Questionnaire { Id = self, Title = "El que edito", Status = QuestionnaireStatus.Draft, SpecialtyCode = "reuma" };
            _repo.Setup(r => r.SearchAsync(null, null, "reuma", 0, 200))
                 .ReturnsAsync((IReadOnlyList<Questionnaire>)new[] { draft, active, retired, itself }.ToList());

            var result = await _sut.GetImportCandidatesAsync("reuma", self);

            Assert.Equal(new[] { draft.Id, active.Id }, result.Select(q => q.Id));
        }

        // Sin excludeId (cuestionario nuevo todavía sin id real) igual filtra los archivados.
        [Fact]
        public async Task GetImportCandidates_SinExcludeId_SoloFiltraArchivados()
        {
            var active = new Questionnaire { Id = Guid.NewGuid(), Status = QuestionnaireStatus.Active, SpecialtyCode = "cardio" };
            var retired = new Questionnaire { Id = Guid.NewGuid(), Status = QuestionnaireStatus.Retired, SpecialtyCode = "cardio" };
            _repo.Setup(r => r.SearchAsync(null, null, "cardio", 0, 200))
                 .ReturnsAsync((IReadOnlyList<Questionnaire>)new[] { active, retired }.ToList());

            var result = await _sut.GetImportCandidatesAsync("cardio", null);

            Assert.Equal(new[] { active.Id }, result.Select(q => q.Id));
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

        // La codificación clínica del cuestionario se persiste igual que el resto: viene en el
        // patch y queda guardada.
        [Fact]
        public async Task UpdateAsync_PersistsQuestionnaireCoding()
        {
            var id = Guid.NewGuid();
            var existing = new Questionnaire { Id = id, Title = "old", Status = QuestionnaireStatus.Draft };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(existing);

            var coding = "{\"system\":\"http://snomed.info/sct\",\"code\":\"302760001\",\"term\":\"control\"}";
            var updated = await _sut.UpdateAsync(id, new Questionnaire { Title = "old", CodingJson = coding }, "u");

            Assert.Equal(coding, updated.CodingJson);
        }

        // A diferencia de título/descripción, la codificación es OPCIONAL y tiene que poder
        // QUITARSE: un patch sin código ni término (null) la borra, no la conserva. Es la
        // paridad con la codificación por pregunta, donde quitarla también es posible.
        [Fact]
        public async Task UpdateAsync_NullCoding_ClearsExistingCoding()
        {
            var id = Guid.NewGuid();
            var existing = new Questionnaire
            {
                Id = id, Title = "old", Status = QuestionnaireStatus.Draft,
                CodingJson = "{\"code\":\"302760001\"}"
            };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(existing);

            var updated = await _sut.UpdateAsync(id, new Questionnaire { Title = "old", CodingJson = null }, "u");

            Assert.Null(updated.CodingJson);
        }

        // Publicar un cuestionario inexistente debe fallar.
        [Fact]
        public async Task PublishAsync_NotFound_Throws()
        {
            _repo.Setup(r => r.GetAsync(It.IsAny<Guid>())).ReturnsAsync((Questionnaire)null);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _sut.PublishAsync(Guid.NewGuid(), "u"));
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
                () => _sut.PublishAsync(id, "u"));
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

            // Tipo especifico: no se publico por VALIDACION, no por una regla de negocio.
            // El controller responde distinto —un issue por regla incumplida— y esa
            // distincion tiene que quedar fijada acá.
            var ex = await Assert.ThrowsAsync<ValidationFailedException>(
                () => _sut.PublishAsync(id, "u"));
            Assert.Contains("missing title", ex.Errors);

            _repo.Verify(r => r.PublishAsync(It.IsAny<Questionnaire>(), It.IsAny<QuestionnaireVersion>()), Times.Never);
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

            var result = await _sut.PublishAsync(id, "u");

            Assert.Equal(QuestionnaireStatus.Active, result.Status);
            Assert.NotNull(result.CurrentVersionId);
            // Versión y cabecera van juntas en una sola llamada transaccional: antes eran
            // dos SaveChanges y una caída en el medio dejaba la versión huérfana.
            _repo.Verify(r => r.PublishAsync(q, It.Is<QuestionnaireVersion>(v => v.VersionNumber == 1)), Times.Once);
            _repo.Verify(r => r.UpdateAsync(It.IsAny<Questionnaire>()), Times.Never);
        }

        // Publicar desde el listado no manda JSON: el service lo arma desde la base. Sin esto
        // el validador rechazaba "Empty resource" y el cuestionario se quedaba en borrador.
        [Fact]
        public async Task PublishAsync_NullFhirJson_BuildsResourceFromEntity()
        {
            var id = Guid.NewGuid();
            var q = Draftable(id);
            q.Title = "Reumato basal";
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(q);
            _validator.Setup(v => v.Validate(It.IsAny<string>(), It.IsAny<string>()))
                      .Returns(TestHelpers.ValidOk());

            QuestionnaireVersion saved = null;
            _repo.Setup(r => r.PublishAsync(It.IsAny<Questionnaire>(), It.IsAny<QuestionnaireVersion>()))
                 .Callback<Questionnaire, QuestionnaireVersion>((_, v) => saved = v)
                 .Returns(Task.CompletedTask);

            var result = await _sut.PublishAsync(id, "u");

            Assert.Equal(QuestionnaireStatus.Active, result.Status);
            Assert.NotNull(saved);
            Assert.Contains("\"resourceType\":\"Questionnaire\"", saved.FhirJson);
            Assert.Contains("Reumato basal", saved.FhirJson);
            _validator.Verify(v => v.Validate(It.Is<string>(s => !string.IsNullOrWhiteSpace(s)), It.IsAny<string>()), Times.Once);
        }

        // Editar la estructura reemplaza secciones/preguntas guardadas: antes UpdateAsync
        // sólo tocaba título/descripción y los cambios del editor se perdían.
        [Fact]
        public async Task UpdateAsync_WithStructure_ReplacesQuestions()
        {
            var id = Guid.NewGuid();
            var existing = new Questionnaire { Id = id, Title = "old", Status = QuestionnaireStatus.Draft };
            existing.Questions.Add(new Question { Id = Guid.NewGuid(), LinkId = "vieja" });
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(existing);

            var patch = new Questionnaire { Title = "nuevo" };
            patch.Groups.Add(new QuestionGroup
            {
                Title = "Sección",
                Questions = { new Question { LinkId = "nueva", Text = "¿Dolor?" } }
            });

            await _sut.UpdateAsync(id, patch, "u");

            _repo.Verify(r => r.UpdateWithStructureAsync(
                existing,
                It.Is<System.Collections.Generic.ICollection<QuestionGroup>>(g => g.Count == 1),
                It.IsAny<System.Collections.Generic.ICollection<Question>>()), Times.Once);
        }

        // Un patch sin estructura (el binder deja las colecciones vacías) no borra lo guardado.
        [Fact]
        public async Task UpdateAsync_WithoutStructure_KeepsQuestions()
        {
            var id = Guid.NewGuid();
            var existing = new Questionnaire { Id = id, Title = "old", Status = QuestionnaireStatus.Draft };
            existing.Questions.Add(new Question { Id = Guid.NewGuid(), LinkId = "vieja" });
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(existing);

            await _sut.UpdateAsync(id, new Questionnaire { Title = "nuevo" }, "u");

            _repo.Verify(r => r.UpdateWithStructureAsync(
                It.IsAny<Questionnaire>(),
                It.IsAny<System.Collections.Generic.ICollection<QuestionGroup>>(),
                It.IsAny<System.Collections.Generic.ICollection<Question>>()), Times.Never);
            Assert.Single(existing.Questions);
        }

        // Publicar apaga el flag de pendientes: lo que estaba sin publicar acaba de quedar
        // congelado en la versión nueva.
        [Fact]
        public async Task PublishAsync_ClearsPendingChanges()
        {
            var id = Guid.NewGuid();
            var q = Draftable(id);
            q.HasPendingChanges = true;
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(q);
            _validator.Setup(v => v.Validate(It.IsAny<string>(), It.IsAny<string>()))
                      .Returns(TestHelpers.ValidOk());

            QuestionnaireVersion saved = null;
            _repo.Setup(r => r.PublishAsync(It.IsAny<Questionnaire>(), It.IsAny<QuestionnaireVersion>()))
                 .Callback<Questionnaire, QuestionnaireVersion>((_, v) => saved = v)
                 .Returns(Task.CompletedTask);

            var result = await _sut.PublishAsync(id, "u");

            Assert.False(result.HasPendingChanges);
            Assert.False(result.HasUnpublishedChanges());
            Assert.Equal(saved.PublishedAt, result.UpdatedAt);
        }

        // Editar prende el flag: es lo que hace visible el "cambios sin publicar".
        [Fact]
        public async Task UpdateAsync_SetsPendingChanges()
        {
            var id = Guid.NewGuid();
            var existing = new Questionnaire
            {
                Id = id,
                Title = "old",
                Status = QuestionnaireStatus.Active,
                CurrentVersionId = Guid.NewGuid(),
                HasPendingChanges = false
            };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(existing);

            var updated = await _sut.UpdateAsync(id, new Questionnaire { Title = "nuevo" }, "u");

            Assert.True(updated.HasPendingChanges);
            Assert.True(updated.HasUnpublishedChanges());
        }

        // Concurrencia optimista: si el cliente trae una versión vieja (alguien más guardó
        // en el medio), la edición se rechaza en vez de pisar el trabajo ajeno.
        [Fact]
        public async Task UpdateAsync_StaleVersion_ThrowsConflict_AndTouchesNothing()
        {
            var id = Guid.NewGuid();
            var existing = new Questionnaire { Id = id, Title = "de otra persona", ConcurrencyVersion = 7 };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(existing);

            var patch = new Questionnaire { Title = "lo mio" };
            patch.Groups.Add(new QuestionGroup { Title = "S", Questions = { new Question { LinkId = "x" } } });

            var ex = await Assert.ThrowsAsync<ConcurrencyConflictException>(
                () => _sut.UpdateAsync(id, patch, "u", expectedVersion: 5));

            Assert.Equal(5, ex.ExpectedVersion);
            Assert.Equal(7, ex.ActualVersion);
            // Lo importante: no se escribió nada. El titulo ajeno queda intacto.
            Assert.Equal("de otra persona", existing.Title);
            _repo.Verify(r => r.UpdateWithStructureAsync(It.IsAny<Questionnaire>(),
                It.IsAny<System.Collections.Generic.ICollection<QuestionGroup>>(),
                It.IsAny<System.Collections.Generic.ICollection<Question>>()), Times.Never);
            _repo.Verify(r => r.UpdateAsync(It.IsAny<Questionnaire>()), Times.Never);
        }

        // Con la versión correcta la edición pasa y el contador avanza, para que el próximo
        // guardado de otra pestaña con la version vieja choque.
        [Fact]
        public async Task UpdateAsync_MatchingVersion_SucceedsAndBumpsVersion()
        {
            var id = Guid.NewGuid();
            var existing = new Questionnaire { Id = id, Title = "old", ConcurrencyVersion = 5 };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(existing);

            var updated = await _sut.UpdateAsync(id, new Questionnaire { Title = "nuevo" }, "u", expectedVersion: 5);

            Assert.Equal("nuevo", updated.Title);
            Assert.Equal(6, updated.ConcurrencyVersion);
        }

        // Sin expectedVersion (llamadas internas) no se chequea nada: el guard vive en la
        // capa HTTP, que exige If-Match.
        [Fact]
        public async Task UpdateAsync_NoExpectedVersion_SkipsCheck()
        {
            var id = Guid.NewGuid();
            var existing = new Questionnaire { Id = id, Title = "old", ConcurrencyVersion = 9 };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(existing);

            var updated = await _sut.UpdateAsync(id, new Questionnaire { Title = "nuevo" }, "u");

            Assert.Equal("nuevo", updated.Title);
            Assert.Equal(10, updated.ConcurrencyVersion);
        }

        // Publicar tambien avanza el contador: congela una version y deja obsoleto
        // cualquier editor que estuviera abierto sobre el estado anterior.
        [Fact]
        public async Task PublishAsync_BumpsConcurrencyVersion()
        {
            var id = Guid.NewGuid();
            var q = Draftable(id);
            q.ConcurrencyVersion = 3;
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(q);
            _validator.Setup(v => v.Validate(It.IsAny<string>(), It.IsAny<string>()))
                      .Returns(TestHelpers.ValidOk());

            var result = await _sut.PublishAsync(id, "u");

            Assert.Equal(4, result.ConcurrencyVersion);
        }

        // Regla de la spec: con respuestas cargadas NO se elimina. Se archiva, porque si
        // no esas respuestas quedan apuntando a un cuestionario que desaparecio del listado.
        [Fact]
        public async Task DeleteAsync_WithResponses_ArchivesInsteadOfDeleting()
        {
            var id = Guid.NewGuid();
            var q = new Questionnaire { Id = id, Status = QuestionnaireStatus.Active };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(q);
            _responses.Setup(r => r.HasResponsesAsync(id)).ReturnsAsync(true);

            var outcome = await _sut.DeleteAsync(id, "u");

            Assert.Equal(DeleteOutcome.ArchivedBecauseInUse, outcome);
            Assert.Equal(QuestionnaireStatus.Retired, q.Status);
            _repo.Verify(r => r.SoftDeleteAsync(It.IsAny<Guid>()), Times.Never);
        }

        // Sin respuestas si se puede dar de baja.
        [Fact]
        public async Task DeleteAsync_WithoutResponses_SoftDeletes()
        {
            var id = Guid.NewGuid();
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(new Questionnaire { Id = id });
            _responses.Setup(r => r.HasResponsesAsync(id)).ReturnsAsync(false);

            var outcome = await _sut.DeleteAsync(id, "u");

            Assert.Equal(DeleteOutcome.Deleted, outcome);
            _repo.Verify(r => r.SoftDeleteAsync(id), Times.Once);
        }

        // Clonar copia la estructura completa pero arranca limpio: Borrador, sin versiones
        // publicadas y con id propio.
        [Fact]
        public async Task CloneAsync_CopiesStructure_AsFreshDraft()
        {
            var id = Guid.NewGuid();
            var groupId = Guid.NewGuid();
            var source = new Questionnaire
            {
                Id = id,
                Title = "Original",
                SpecialtyCode = "REU",
                CodingJson = "{\"system\":\"http://snomed.info/sct\",\"code\":\"302760001\",\"term\":\"control\"}",
                Status = QuestionnaireStatus.Active,
                CurrentVersionId = Guid.NewGuid()
            };
            source.Versions.Add(new QuestionnaireVersion { Id = source.CurrentVersionId.Value, VersionNumber = 1 });
            source.Groups.Add(new QuestionGroup { Id = groupId, Title = "Sección", OrderIndex = 0 });
            source.Questions.Add(new Question
            {
                Id = Guid.NewGuid(),
                GroupId = groupId,
                LinkId = "edad",
                Text = "Edad",
                Type = QuestionType.Integer,
                Required = true,
                ValidationJson = "{\"min\":0,\"max\":120}",
                Options = { new AnswerOption { Value = "x", Display = "X" } }
            });
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(source);

            var copy = await _sut.CloneAsync(id, "Copia nueva", "u");

            Assert.NotEqual(id, copy.Id);
            Assert.Equal("Copia nueva", copy.Title);
            Assert.Equal("REU", copy.SpecialtyCode);
            Assert.Equal(QuestionnaireStatus.Draft, copy.Status);
            Assert.Null(copy.CurrentVersionId);
            Assert.Empty(copy.Versions);
            // La codificación del cuestionario también se clona: un clon comparte qué mide.
            Assert.Equal(source.CodingJson, copy.CodingJson);

            var clonedQuestion = copy.Groups.Single().Questions.Single();
            Assert.Equal("edad", clonedQuestion.LinkId);          // linkId se conserva
            Assert.Equal("{\"min\":0,\"max\":120}", clonedQuestion.ValidationJson);
            Assert.Single(clonedQuestion.Options);
            _repo.Verify(r => r.AddAsync(It.IsAny<Questionnaire>()), Times.Once);
        }

        // Sin titulo explicito, el clon se distingue solo.
        [Fact]
        public async Task CloneAsync_WithoutTitle_SuffixesCopia()
        {
            var id = Guid.NewGuid();
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(new Questionnaire { Id = id, Title = "Reumato" });

            var copy = await _sut.CloneAsync(id, null, "u");

            Assert.Equal("Reumato (copia)", copy.Title);
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
