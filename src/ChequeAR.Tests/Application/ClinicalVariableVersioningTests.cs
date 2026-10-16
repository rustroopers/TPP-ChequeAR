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
    public class ClinicalVariableVersioningTests
    {
        private readonly Mock<IClinicalVariableRepository> _repo = new Mock<IClinicalVariableRepository>();
        private readonly ClinicalVariableService _sut;

        public ClinicalVariableVersioningTests()
        {
            _sut = new ClinicalVariableService(_repo.Object, TestHelpers.NoopAuditDispatcher(), TestHelpers.UnrestrictedAccess());
        }

        private ClinicalVariable Severidad(int revision = 1, ClinicalVariableVersion vigente = null)
        {
            var v = new ClinicalVariable
            {
                Id = Guid.NewGuid(), Code = "severidad", Name = "Severidad", Type = QuestionType.Choice, IsActive = true,
                Revision = revision, CurrentVersionId = vigente?.Id,
                Options =
                {
                    new VariableOption { Id = Guid.NewGuid(), Value = "leve", Display = "Leve", Code = "leve", OrderIndex = 0 },
                    new VariableOption { Id = Guid.NewGuid(), Value = "grave", Display = "Grave", Code = "grave", OrderIndex = 1 }
                }
            };
            if (vigente != null)
            {
                vigente.VariableId = v.Id;
                if (vigente.DefinitionJson == null) vigente.DefinitionJson = VariableDefinition.From(v).ToJson();
                _repo.Setup(r => r.GetVersionAsync(vigente.Id)).ReturnsAsync(vigente);
                _repo.Setup(r => r.ListVersionsAsync(v.Id)).ReturnsAsync(new List<ClinicalVariableVersion> { vigente });
            }
            else
            {
                _repo.Setup(r => r.ListVersionsAsync(v.Id)).ReturnsAsync(new List<ClinicalVariableVersion>());
            }
            _repo.Setup(r => r.GetAsync(v.Id)).ReturnsAsync(v);
            return v;
        }

        // Activar o desactivar no es un cambio de definición: antes reemplazaba las opciones
        // (ORA-00001) y subía la revisión, y todos los borradores avisaban un cambio falso.
        [Fact]
        public async Task SetActive_SoloGuardaLaCabecera()
        {
            var v = Severidad(revision: 3);

            await _sut.SetActiveAsync(v.Id, false, "admin");

            Assert.False(v.IsActive);
            Assert.Equal(3, v.Revision);
            _repo.Verify(r => r.UpdateHeaderAsync(v), Times.Once);
            _repo.Verify(r => r.UpdateWithOptionsAsync(It.IsAny<ClinicalVariable>(), It.IsAny<ICollection<VariableOption>>()), Times.Never);
        }

        [Fact]
        public async Task Publish_LaPrimeraVez_CreaLaVersion1()
        {
            var v = Severidad(revision: 2);
            ClinicalVariableVersion creada = null;
            _repo.Setup(r => r.AddVersionAsync(v, It.IsAny<ClinicalVariableVersion>()))
                 .Callback<ClinicalVariable, ClinicalVariableVersion>((_, x) => creada = x).Returns(Task.CompletedTask);

            await _sut.PublishAsync(v.Id, "admin", " Alta ");

            Assert.Equal(1, creada.VersionNumber);
            Assert.Equal(3, creada.SourceRevision);
            Assert.Equal("Alta", creada.ChangeNote);
            Assert.Equal(creada.Id, v.CurrentVersionId);
            Assert.Equal(2, VariableDefinition.Parse(creada.DefinitionJson).Options.Count);
        }

        [Fact]
        public async Task Publish_SinCambios_NoCreaVersion()
        {
            var vigente = new ClinicalVariableVersion { Id = Guid.NewGuid(), VersionNumber = 1, SourceRevision = 1 };
            var v = Severidad(revision: 2, vigente: vigente);

            var result = await _sut.PublishAsync(v.Id, "admin");

            Assert.Same(vigente, result);
            Assert.Equal(2, vigente.SourceRevision);
            _repo.Verify(r => r.AddVersionAsync(It.IsAny<ClinicalVariable>(), It.IsAny<ClinicalVariableVersion>()), Times.Never);
        }

        [Fact]
        public async Task Publish_ConCambios_CreaLaSiguiente()
        {
            var vigente = new ClinicalVariableVersion { Id = Guid.NewGuid(), VersionNumber = 1, SourceRevision = 1 };
            var v = Severidad(revision: 2, vigente: vigente);
            v.Options.Add(new VariableOption { Value = "moderada", Display = "Moderada", Code = "moderada", OrderIndex = 2 });
            ClinicalVariableVersion creada = null;
            _repo.Setup(r => r.AddVersionAsync(v, It.IsAny<ClinicalVariableVersion>()))
                 .Callback<ClinicalVariable, ClinicalVariableVersion>((_, x) => creada = x).Returns(Task.CompletedTask);

            await _sut.PublishAsync(v.Id, "admin");

            Assert.Equal(2, creada.VersionNumber);
            Assert.Equal(creada.Id, v.CurrentVersionId);
        }

        [Fact]
        public async Task Publish_ConRevisionVieja_Rechaza()
        {
            var v = Severidad(revision: 4);

            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => _sut.PublishAsync(v.Id, "admin", expectedRevision: 3));

            _repo.Verify(r => r.AddVersionAsync(It.IsAny<ClinicalVariable>(), It.IsAny<ClinicalVariableVersion>()), Times.Never);
        }

        // Mover la revisión al publicar es lo que hace chocar dos publicaciones simultáneas.
        [Fact]
        public async Task Publish_MueveLaRevision()
        {
            var v = Severidad(revision: 4);
            ClinicalVariableVersion creada = null;
            _repo.Setup(r => r.AddVersionAsync(v, It.IsAny<ClinicalVariableVersion>()))
                 .Callback<ClinicalVariable, ClinicalVariableVersion>((_, x) => creada = x).Returns(Task.CompletedTask);

            await _sut.PublishAsync(v.Id, "admin");

            Assert.Equal(5, v.Revision);
            Assert.Equal(5, creada.SourceRevision);
        }

        [Fact]
        public async Task Publish_NotaDemasiadoLarga_Rechaza()
        {
            var v = Severidad();

            await Assert.ThrowsAsync<ValidationFailedException>(() => _sut.PublishAsync(v.Id, "admin", new string('x', 2001)));
        }

        [Theory]
        [InlineData("otro-codigo", false)]
        [InlineData("severidad", true)]
        public async Task Update_CodigoORepeticionEnUnaPublicada_Rechaza(string code, bool repeats)
        {
            var v = Severidad(vigente: new ClinicalVariableVersion { Id = Guid.NewGuid(), VersionNumber = 1, SourceRevision = 1 });
            var patch = new ClinicalVariable { Code = code, Name = "Severidad", Type = QuestionType.Choice, RepeatsAllowed = repeats, Options = v.Options.ToList() };

            await Assert.ThrowsAsync<ValidationFailedException>(() => _sut.UpdateAsync(v.Id, patch, "admin"));
        }

        [Fact]
        public async Task Delete_SinPublicarPeroEnUso_Desactiva()
        {
            var v = Severidad();
            _repo.Setup(r => r.CountQuestionsUsingAsync(v.Id)).ReturnsAsync(2);

            var outcome = await _sut.DeleteAsync(v.Id, "admin");

            Assert.Equal(VariableDeleteOutcome.DeactivatedBecauseInUse, outcome);
            _repo.Verify(r => r.DeleteAsync(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task Update_ConRevisionVieja_RechazaSinGuardar()
        {
            var v = Severidad(revision: 5);
            var patch = new ClinicalVariable { Code = "severidad", Name = "Severidad", Type = QuestionType.Choice, Options = v.Options.ToList() };

            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => _sut.UpdateAsync(v.Id, patch, "admin", expectedRevision: 4));

            _repo.Verify(r => r.UpdateWithOptionsAsync(It.IsAny<ClinicalVariable>(), It.IsAny<ICollection<VariableOption>>()), Times.Never);
        }

        [Fact]
        public async Task Update_CambioDeTipoEnUnaPublicada_Rechaza()
        {
            var v = Severidad(vigente: new ClinicalVariableVersion { Id = Guid.NewGuid(), VersionNumber = 1, SourceRevision = 1 });
            var patch = new ClinicalVariable { Code = "severidad", Name = "Severidad", Type = QuestionType.String };

            var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => _sut.UpdateAsync(v.Id, patch, "admin"));

            Assert.Contains(ex.Errors, e => e.Contains("tipo"));
        }

        [Fact]
        public async Task Update_CambioDeTipoSinPublicar_SePermite()
        {
            var v = Severidad();
            var patch = new ClinicalVariable { Code = "severidad", Name = "Severidad", Type = QuestionType.String };

            var updated = await _sut.UpdateAsync(v.Id, patch, "admin");

            Assert.Equal(QuestionType.String, updated.Type);
        }

        [Fact]
        public async Task Delete_Publicada_DesactivaEnVezDeBorrar()
        {
            var v = Severidad(vigente: new ClinicalVariableVersion { Id = Guid.NewGuid(), VersionNumber = 1, SourceRevision = 1 });

            var outcome = await _sut.DeleteAsync(v.Id, "admin");

            Assert.Equal(VariableDeleteOutcome.DeactivatedBecauseInUse, outcome);
            Assert.False(v.IsActive);
            _repo.Verify(r => r.DeleteAsync(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task Delete_SinPublicarNiUsar_Borra()
        {
            var v = Severidad();

            var outcome = await _sut.DeleteAsync(v.Id, "admin");

            Assert.Equal(VariableDeleteOutcome.Deleted, outcome);
            _repo.Verify(r => r.DeleteAsync(v.Id), Times.Once);
        }

        [Fact]
        public async Task Publicacion_CambiosSinPublicarSeDerivanDeLaRevision()
        {
            var publicada = Severidad(revision: 2);
            var editada = Severidad(revision: 3);
            var nueva = Severidad();
            _repo.Setup(r => r.GetCurrentVersionHeadersAsync(It.IsAny<IEnumerable<Guid>>()))
                 .ReturnsAsync(new Dictionary<Guid, ClinicalVariableVersion>
                 {
                     [publicada.Id] = new ClinicalVariableVersion { VersionNumber = 1, SourceRevision = 2 },
                     [editada.Id] = new ClinicalVariableVersion { VersionNumber = 4, SourceRevision = 2 }
                 });

            var estado = await _sut.GetPublicationAsync(new[] { publicada, editada, nueva });

            Assert.False(estado[publicada.Id].HasUnpublishedChanges);
            Assert.True(estado[editada.Id].HasUnpublishedChanges);
            Assert.Equal(4, estado[editada.Id].CurrentVersion);
            Assert.Equal(0, estado[nueva.Id].CurrentVersion);
        }
    }
}
