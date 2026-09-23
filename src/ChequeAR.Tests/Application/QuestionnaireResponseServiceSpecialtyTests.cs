using System;
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
    /// <summary>Autorización por especialidad en QuestionnaireResponseService (feature 011).</summary>
    public class QuestionnaireResponseServiceSpecialtyTests
    {
        private readonly Mock<IQuestionnaireResponseRepository> _repo = new Mock<IQuestionnaireResponseRepository>();
        private readonly Mock<IQuestionnaireRepository> _qRepo = new Mock<IQuestionnaireRepository>();
        private readonly Mock<IFhirValidator> _validator = new Mock<IFhirValidator>();

        private QuestionnaireResponseService Sut(ChequeAR.Application.Security.ISpecialtyGuard guard, ChequeAR.Application.Security.IAccessGuard access = null)
            => new QuestionnaireResponseService(_repo.Object, _qRepo.Object, _validator.Object, TestHelpers.NoopAuditDispatcher(), guard,
                access ?? TestHelpers.UnrestrictedAccess());

        [Fact]
        public async Task GetAsync_CuestionarioFueraDeScope_Forbidden()
        {
            var qId = Guid.NewGuid();
            var rId = Guid.NewGuid();
            _repo.Setup(r => r.GetAsync(rId)).ReturnsAsync(new QuestionnaireResponse { Id = rId, QuestionnaireId = qId });
            _qRepo.Setup(r => r.GetSpecialtyCodeAsync(qId)).ReturnsAsync("onco");
            var sut = Sut(TestHelpers.GuardFor("cardio"));

            await Assert.ThrowsAsync<SpecialtyForbiddenException>(() => sut.GetAsync(rId));
        }

        [Fact]
        public async Task GetAsync_CuestionarioDentroDeScope_Devuelve()
        {
            var qId = Guid.NewGuid();
            var rId = Guid.NewGuid();
            _repo.Setup(r => r.GetAsync(rId)).ReturnsAsync(new QuestionnaireResponse { Id = rId, QuestionnaireId = qId });
            _qRepo.Setup(r => r.GetSpecialtyCodeAsync(qId)).ReturnsAsync("cardio");
            var sut = Sut(TestHelpers.GuardFor("cardio"));

            var result = await sut.GetAsync(rId);

            Assert.NotNull(result);
        }

        [Fact]
        public async Task CreateAsync_ContraCuestionarioAjeno_Forbidden()
        {
            var qId = Guid.NewGuid();
            var q = new Questionnaire { Id = qId, SpecialtyCode = "onco", Status = QuestionnaireStatus.Draft };
            q.Versions.Add(new QuestionnaireVersion { Id = Guid.NewGuid(), VersionNumber = 1 });
            _qRepo.Setup(r => r.GetAsync(qId)).ReturnsAsync(q);
            var sut = Sut(TestHelpers.GuardFor("cardio"));

            await Assert.ThrowsAsync<SpecialtyForbiddenException>(
                () => sut.CreateAsync(new QuestionnaireResponse { QuestionnaireId = qId, PatientHospitalId = "HC1" }, "u"));
        }

        [Fact]
        public async Task AcquireLockAsync_FueraDeScope_Lanza_SinTomarElLock()
        {
            // El test del DoS de 15 minutos: si el guard corriera DESPUÉS de tomar el lock,
            // un usuario no autorizado dejaría la ficha bloqueada aunque reciba el error.
            var qId = Guid.NewGuid();
            var rId = Guid.NewGuid();
            _repo.Setup(r => r.GetAsync(rId)).ReturnsAsync(new QuestionnaireResponse
            {
                Id = rId, QuestionnaireId = qId, Status = ResponseStatus.InProgress
            });
            _qRepo.Setup(r => r.GetSpecialtyCodeAsync(qId)).ReturnsAsync("onco");
            var sut = Sut(TestHelpers.GuardFor("cardio"));

            await Assert.ThrowsAsync<SpecialtyForbiddenException>(() => sut.AcquireLockAsync(rId, "u"));

            _repo.Verify(r => r.UpdateAsync(It.IsAny<QuestionnaireResponse>()), Times.Never);
        }

        [Fact]
        public async Task SaveProgressAsync_FueraDeScope_Forbidden()
        {
            var qId = Guid.NewGuid();
            var rId = Guid.NewGuid();
            _repo.Setup(r => r.GetAsync(rId)).ReturnsAsync(new QuestionnaireResponse
            {
                Id = rId, QuestionnaireId = qId, Status = ResponseStatus.InProgress
            });
            _qRepo.Setup(r => r.GetSpecialtyCodeAsync(qId)).ReturnsAsync("onco");
            var sut = Sut(TestHelpers.GuardFor("cardio"));

            await Assert.ThrowsAsync<SpecialtyForbiddenException>(
                () => sut.SaveProgressAsync(rId, "u", Guid.NewGuid(), null, null));
        }

        [Fact]
        public async Task FinalizeAsync_FueraDeScope_Forbidden()
        {
            var qId = Guid.NewGuid();
            var rId = Guid.NewGuid();
            _repo.Setup(r => r.GetAsync(rId)).ReturnsAsync(new QuestionnaireResponse
            {
                Id = rId, QuestionnaireId = qId, Status = ResponseStatus.InProgress
            });
            _qRepo.Setup(r => r.GetSpecialtyCodeAsync(qId)).ReturnsAsync("onco");
            var sut = Sut(TestHelpers.GuardFor("cardio"));

            await Assert.ThrowsAsync<SpecialtyForbiddenException>(
                () => sut.FinalizeAsync(rId, "u", Guid.NewGuid()));
        }
    }
}
