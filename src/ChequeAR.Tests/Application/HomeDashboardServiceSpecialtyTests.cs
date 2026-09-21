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
}
