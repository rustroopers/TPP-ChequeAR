using System;
using System.Collections.Generic;
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
    /// <summary>
    /// "Fichas recientes" del inicio no pasaba scope al repositorio, así que un Jefe de
    /// general veía en la home fichas de cardio/onco/etc. Acá queda fijado que el scope del
    /// guard llega a la consulta (y que Administrador lo sigue viendo todo).
    /// </summary>
    public class HomeDashboardServiceSpecialtyTests
    {
        private readonly Mock<IQuestionnaireResponseRepository> _responses = new Mock<IQuestionnaireResponseRepository>();
        private readonly Mock<IQuestionnaireRepository> _questionnaires = new Mock<IQuestionnaireRepository>();
        private readonly Mock<IUserRepository> _users = new Mock<IUserRepository>();

        private HomeDashboardService Sut(ISpecialtyGuard guard)
            => new HomeDashboardService(_responses.Object, _questionnaires.Object, _users.Object,
                                        TestHelpers.CatalogWith(), guard);

        [Fact]
        public async Task RecentEncountersAsync_Jefe_ConsultaLimitadaASusEspecialidades()
        {
            SpecialtyScope capturado = null;
            _responses.Setup(r => r.SearchAsync(null, null, null, 0, It.IsAny<int>(), It.IsAny<SpecialtyScope>()))
                .Callback<string, Guid?, ResponseStatus?, int, int, SpecialtyScope>(
                    (p, q, s, skip, take, scope) => capturado = scope)
                .ReturnsAsync((IReadOnlyList<QuestionnaireResponse>)new List<QuestionnaireResponse>());

            await Sut(TestHelpers.GuardFor("general")).RecentEncountersAsync();

            Assert.NotNull(capturado);
            Assert.False(capturado.IsUnrestricted);
            Assert.True(capturado.Allows("general"));
            Assert.False(capturado.Allows("cardio"));
        }

        [Fact]
        public async Task RecentEncountersAsync_Administrador_ConsultaSinRestriccion()
        {
            SpecialtyScope capturado = null;
            _responses.Setup(r => r.SearchAsync(null, null, null, 0, It.IsAny<int>(), It.IsAny<SpecialtyScope>()))
                .Callback<string, Guid?, ResponseStatus?, int, int, SpecialtyScope>(
                    (p, q, s, skip, take, scope) => capturado = scope)
                .ReturnsAsync((IReadOnlyList<QuestionnaireResponse>)new List<QuestionnaireResponse>());

            await Sut(TestHelpers.UnrestrictedGuard()).RecentEncountersAsync();

            Assert.NotNull(capturado);
            Assert.True(capturado.IsUnrestricted);
        }
    }

    public class HomeDashboardServiceTests
    {
        private readonly Mock<IQuestionnaireResponseRepository> _responses = new Mock<IQuestionnaireResponseRepository>();
        private readonly Mock<IQuestionnaireRepository> _questionnaires = new Mock<IQuestionnaireRepository>();
        private readonly Mock<IUserRepository> _users = new Mock<IUserRepository>();

        private HomeDashboardService Sut()
            => new HomeDashboardService(_responses.Object, _questionnaires.Object, _users.Object,
                                        TestHelpers.CatalogWith(), TestHelpers.UnrestrictedGuard());

        // Título y especialidad salen de una sola consulta liviana; sin nombre embebido, la HC.
        [Fact]
        public async Task RecentEncounters_ResuelveCuestionarioYPaciente()
        {
            var qid = Guid.NewGuid();
            _responses.Setup(r => r.SearchAsync(null, null, null, 0, 5, It.IsAny<SpecialtyScope>()))
                .ReturnsAsync((IReadOnlyList<QuestionnaireResponse>)new List<QuestionnaireResponse>
                {
                    new QuestionnaireResponse { Id = Guid.NewGuid(), QuestionnaireId = qid, PatientHospitalId = "PAC-1", Status = ResponseStatus.Completed },
                    new QuestionnaireResponse { Id = Guid.NewGuid(), QuestionnaireId = Guid.NewGuid(), PatientHospitalId = "PAC-2" }
                });
            _questionnaires.Setup(q => q.GetHeadersAsync(It.IsAny<IEnumerable<Guid>>()))
                .ReturnsAsync((IReadOnlyList<Questionnaire>)new List<Questionnaire> { new Questionnaire { Id = qid, Title = "Control", SpecialtyCode = "cardio" } });

            var recientes = await Sut().RecentEncountersAsync();

            Assert.Equal("Control", recientes[0].QuestionnaireTitle);
            Assert.Equal("PAC-1", recientes[0].PatientName);
            Assert.Equal(recientes[1].QuestionnaireId.ToString(), recientes[1].QuestionnaireTitle);
            _questionnaires.Verify(q => q.GetAsync(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task RecentEncounters_SinFichas_NoConsultaCuestionarios()
        {
            _responses.Setup(r => r.SearchAsync(null, null, null, 0, 5, It.IsAny<SpecialtyScope>()))
                .ReturnsAsync((IReadOnlyList<QuestionnaireResponse>)new List<QuestionnaireResponse>());

            Assert.Empty(await Sut().RecentEncountersAsync());
            _questionnaires.Verify(q => q.GetHeadersAsync(It.IsAny<IEnumerable<Guid>>()), Times.Never);
        }

        [Fact]
        public async Task Kpis_CuentaEnLaBase()
        {
            _questionnaires.Setup(q => q.CountAsync(null, null, null)).ReturnsAsync(4);
            _users.Setup(u => u.CountAsync(null, null, null, true)).ReturnsAsync(12);
            _responses.Setup(r => r.CountAsync(null, null, null, null)).ReturnsAsync(250);
            _questionnaires.Setup(q => q.GetSpecialtyCodesAsync()).ReturnsAsync((IReadOnlyList<string>)new List<string> { "cardio", "onco" });

            var k = await Sut().KpisAsync();

            Assert.Equal(4, k.Questionnaires);
            Assert.Equal(12, k.Users);
            Assert.Equal(250, k.Encounters);
            Assert.Equal(2, k.Specialties);
        }
    }
}
