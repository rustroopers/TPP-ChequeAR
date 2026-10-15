using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Security;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using Moq;
using Xunit;

namespace ChequeAR.Tests.Application
{
    public class PatientServiceScopeTests
    {
        private readonly Mock<IQuestionnaireResponseRepository> _responses = new Mock<IQuestionnaireResponseRepository>();

        private PatientService Sut(ISpecialtyGuard guard) => new PatientService(
            Mock.Of<IHospitalFhirClient>(), Mock.Of<IOracleViewClient>(), _responses.Object,
            Mock.Of<IQuestionnaireRepository>(), TestHelpers.CatalogWith(), guard);

        private static bool ScopeIs(SpecialtyScope s, params string[] codes)
            => s != null && !s.IsUnrestricted && s.Codes.OrderBy(c => c).SequenceEqual(codes.OrderBy(c => c));

        [Fact]
        public async Task GetEncounters_FiltraPorLasEspecialidadesDeQuienConsulta()
        {
            _responses.Setup(r => r.SearchAsync("HC-1", null, null, 0, 50, It.IsAny<SpecialtyScope>()))
                      .ReturnsAsync(new List<QuestionnaireResponse>());

            await Sut(TestHelpers.GuardFor("cardio", "neumo")).GetEncountersAsync("HC-1");

            _responses.Verify(r => r.SearchAsync("HC-1", null, null, 0, 50,
                It.Is<SpecialtyScope>(s => ScopeIs(s, "cardio", "neumo"))), Times.Once);
        }

        [Fact]
        public async Task CountEncounters_UsaElMismoScope()
        {
            await Sut(TestHelpers.GuardFor("onco")).CountEncountersAsync("HC-1");

            _responses.Verify(r => r.CountAsync("HC-1", null, null,
                It.Is<SpecialtyScope>(s => ScopeIs(s, "onco"))), Times.Once);
        }

        [Fact]
        public async Task GetEncounters_SinEspecialidades_PideScopeVacio()
        {
            _responses.Setup(r => r.SearchAsync(It.IsAny<string>(), null, null, 0, It.IsAny<int>(), It.IsAny<SpecialtyScope>()))
                      .ReturnsAsync(new List<QuestionnaireResponse>());

            await Sut(TestHelpers.GuardFor()).GetEncountersAsync("HC-1");

            _responses.Verify(r => r.SearchAsync("HC-1", null, null, 0, 50,
                It.Is<SpecialtyScope>(s => s.IsEmpty)), Times.Once);
        }
    }
}
