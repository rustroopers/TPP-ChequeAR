using System;
using System.Threading.Tasks;
using ChequeAR.Application.Exceptions;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Security;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using Moq;
using Xunit;

namespace ChequeAR.Tests.Application
{
    /// <summary>
    /// Autorización por especialidad en QuestionnaireService (feature 011). Complementa a
    /// QuestionnaireServiceTests, que usa un guard sin restricción y no ejercita esto.
    /// </summary>
    public class QuestionnaireServiceSpecialtyTests
    {
        private readonly Mock<IQuestionnaireRepository> _repo = new Mock<IQuestionnaireRepository>();
        private readonly Mock<IQuestionnaireResponseRepository> _responses = new Mock<IQuestionnaireResponseRepository>();
        private readonly Mock<IFhirValidator> _validator = new Mock<IFhirValidator>();
        private readonly ClinicalVariable _peso = TestHelpers.Variable("peso");

        private QuestionnaireService Sut(ISpecialtyGuard guard, SpecialtyCatalogService catalog = null, IAccessGuard access = null)
            => new QuestionnaireService(_repo.Object, _responses.Object, _validator.Object,
                TestHelpers.NoopAuditDispatcher(), guard, catalog ?? TestHelpers.CatalogWith(),
                access ?? TestHelpers.UnrestrictedAccess(), TestHelpers.VariableRepoWith(_peso));

        [Fact]
        public async Task GetAsync_FueraDeScope_Lanza()
        {
            var id = Guid.NewGuid();
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(new Questionnaire { Id = id, SpecialtyCode = "onco" });
            var sut = Sut(TestHelpers.GuardFor("cardio"));

            await Assert.ThrowsAsync<SpecialtyForbiddenException>(() => sut.GetAsync(id));
        }

        [Fact]
        public async Task GetAsync_DentroDeScope_Devuelve()
        {
            var id = Guid.NewGuid();
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(new Questionnaire { Id = id, SpecialtyCode = "cardio" });
            var sut = Sut(TestHelpers.GuardFor("cardio"));

            var result = await sut.GetAsync(id);

            Assert.NotNull(result);
        }

        [Fact]
        public async Task CreateAsync_SinEspecialidad_ValidationFailed()
        {
            var sut = Sut(TestHelpers.UnrestrictedGuard());
            await Assert.ThrowsAsync<ValidationFailedException>(
                () => sut.CreateAsync(new Questionnaire { Title = "x" }, "u"));
        }

        [Fact]
        public async Task CreateAsync_EspecialidadFueraDeCatalogo_ValidationFailed()
        {
            var sut = Sut(TestHelpers.UnrestrictedGuard());
            await Assert.ThrowsAsync<ValidationFailedException>(
                () => sut.CreateAsync(new Questionnaire { Title = "x", SpecialtyCode = "hemato" }, "u"));
        }

        [Fact]
        public async Task CreateAsync_EnEspecialidadAjena_Forbidden()
        {
            var sut = Sut(TestHelpers.GuardFor("cardio"));
            await Assert.ThrowsAsync<SpecialtyForbiddenException>(
                () => sut.CreateAsync(new Questionnaire { Title = "x", SpecialtyCode = "onco" }, "u"));
        }

        [Fact]
        public async Task CreateAsync_EnPropiaEspecialidad_Ok()
        {
            var sut = Sut(TestHelpers.GuardFor("cardio"));
            var created = await sut.CreateAsync(new Questionnaire { Title = "x", SpecialtyCode = "cardio" }, "u");
            Assert.Equal("cardio", created.SpecialtyCode);
            Assert.Equal("Cardiología", created.SpecialtyDisplay);
        }

        [Fact]
        public async Task UpdateAsync_MueveDeCardioAOnco_ConGuardSoloCardio_Forbidden()
        {
            // Protege contra el "robo": un cuestionario ajeno no se puede declarar de la
            // propia especialidad (acá probamos el caso simétrico: mover el propio afuera).
            var id = Guid.NewGuid();
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(new Questionnaire { Id = id, SpecialtyCode = "cardio", Status = QuestionnaireStatus.Draft });
            var sut = Sut(TestHelpers.GuardFor("cardio"));

            await Assert.ThrowsAsync<SpecialtyForbiddenException>(
                () => sut.UpdateAsync(id, new Questionnaire { SpecialtyCode = "onco" }, "u"));
        }

        [Fact]
        public async Task UpdateAsync_SobreCuestionarioAjeno_SinCambiarEspecialidad_Forbidden()
        {
            // Protege contra el "robo" real: PUT sobre un cuestionario de onco declarándolo
            // sin cambios, con un usuario que sólo tiene cardio.
            var id = Guid.NewGuid();
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(new Questionnaire { Id = id, SpecialtyCode = "onco", Status = QuestionnaireStatus.Draft });
            var sut = Sut(TestHelpers.GuardFor("cardio"));

            await Assert.ThrowsAsync<SpecialtyForbiddenException>(
                () => sut.UpdateAsync(id, new Questionnaire { Title = "nuevo" }, "u"));
        }

        [Fact]
        public async Task SearchAsync_PasaElScopeAlRepo()
        {
            _repo.Setup(r => r.SearchAsync(null, null, null, 0, 2000))
                 .ReturnsAsync((System.Collections.Generic.IReadOnlyList<Questionnaire>)new System.Collections.Generic.List<Questionnaire>
                 {
                     new Questionnaire { Id = Guid.NewGuid(), SpecialtyCode = "cardio" },
                     new Questionnaire { Id = Guid.NewGuid(), SpecialtyCode = "onco" }
                 });
            var sut = Sut(TestHelpers.GuardFor("cardio"));

            var result = await sut.SearchAsync(null, null, null, 0, 20);

            Assert.Single(result);
            Assert.Equal("cardio", result[0].SpecialtyCode);
        }

        [Fact]
        public async Task SearchAsync_UsuarioSinEspecialidades_ListaVacia()
        {
            var sut = Sut(TestHelpers.GuardFor());   // scope vacío
            var result = await sut.SearchAsync(null, null, null, 0, 20);
            Assert.Empty(result);
            _repo.Verify(r => r.SearchAsync(It.IsAny<QuestionnaireStatus?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task CloneAsync_DeOrigenAjeno_Forbidden()
        {
            var id = Guid.NewGuid();
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(new Questionnaire { Id = id, Title = "x", SpecialtyCode = "onco" });
            var sut = Sut(TestHelpers.GuardFor("cardio"));

            await Assert.ThrowsAsync<SpecialtyForbiddenException>(() => sut.CloneAsync(id, null, "u"));
        }

        [Fact]
        public async Task PublishAsync_DeEspecialidadAjena_Forbidden()
        {
            var id = Guid.NewGuid();
            var q = new Questionnaire { Id = id, SpecialtyCode = "onco", Status = QuestionnaireStatus.Draft };
            q.Questions.Add(new Question());
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(q);
            var sut = Sut(TestHelpers.GuardFor("cardio"));

            await Assert.ThrowsAsync<SpecialtyForbiddenException>(() => sut.PublishAsync(id, "u"));
        }

        // ---- Eje rol: Medico lee, no escribe (independiente del eje especialidad de arriba) ----

        [Fact]
        public async Task CreateAsync_ComoMedico_Lanza()
        {
            var sut = Sut(TestHelpers.UnrestrictedGuard(), access: TestHelpers.AccessGuardFor(Role.Medico));
            var q = new Questionnaire { Title = "x", SpecialtyCode = "cardio" };

            await Assert.ThrowsAsync<PermissionForbiddenException>(() => sut.CreateAsync(q, "med.cardio"));
            _repo.Verify(r => r.AddAsync(It.IsAny<Questionnaire>()), Times.Never);
        }

        [Fact]
        public async Task PublishAsync_ComoMedico_Lanza()
        {
            var id = Guid.NewGuid();
            var sut = Sut(TestHelpers.UnrestrictedGuard(), access: TestHelpers.AccessGuardFor(Role.Medico));

            await Assert.ThrowsAsync<PermissionForbiddenException>(() => sut.PublishAsync(id, "med.cardio"));
            // Ni siquiera debería haber ido a buscar el cuestionario.
            _repo.Verify(r => r.GetAsync(id), Times.Never);
        }

        [Fact]
        public async Task DeleteAsync_ComoJefe_Lanza()
        {
            // Jefe archiva, no borra (ver RolePolicy): QuestionnaireDelete es sólo Admin.
            var id = Guid.NewGuid();
            var sut = Sut(TestHelpers.UnrestrictedGuard(), access: TestHelpers.AccessGuardFor(Role.Jefe));

            await Assert.ThrowsAsync<PermissionForbiddenException>(() => sut.DeleteAsync(id, "jefe.cardio"));
        }

        [Fact]
        public async Task ArchiveAsync_ComoJefe_Permite()
        {
            var id = Guid.NewGuid();
            var q = new Questionnaire { Id = id, SpecialtyCode = "cardio", Status = QuestionnaireStatus.Active };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(q);
            var sut = Sut(TestHelpers.UnrestrictedGuard(), access: TestHelpers.AccessGuardFor(Role.Jefe));

            await sut.ArchiveAsync(id, "jefe.cardio");

            Assert.Equal(QuestionnaireStatus.Retired, q.Status);
        }

        [Fact]
        public async Task GetAsync_ComoMedico_Permite()
        {
            // Medico SÍ puede leer -- sólo se le niega la escritura.
            var id = Guid.NewGuid();
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(new Questionnaire { Id = id, SpecialtyCode = "cardio" });
            var sut = Sut(TestHelpers.UnrestrictedGuard(), access: TestHelpers.AccessGuardFor(Role.Medico));

            var result = await sut.GetAsync(id);

            Assert.NotNull(result);
        }
    }
}
