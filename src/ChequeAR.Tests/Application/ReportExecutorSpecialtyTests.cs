using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ChequeAR.Application.Dtos;
using ChequeAR.Application.Exceptions;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Reports;
using ChequeAR.Domain.Entities;
using Moq;
using Xunit;

namespace ChequeAR.Tests.Application
{
    /// <summary>
    /// ReportExecutor resuelve el cuestionario directo contra IQuestionnaireRepository (no
    /// pasa por QuestionnaireService.GetAsync), así que sin este chequeo propio cualquiera
    /// que conociera el GUID de un cuestionario de otra especialidad podía ejecutar un
    /// reporte sobre él. Este archivo fija que eso ya no es posible.
    /// </summary>
    public class ReportExecutorSpecialtyTests
    {
        private readonly Mock<IQuestionnaireRepository> _repo = new Mock<IQuestionnaireRepository>();
        private readonly Mock<IReportQueryRunner> _runner = new Mock<IReportQueryRunner>();

        private ReportExecutor Sut(ChequeAR.Application.Security.ISpecialtyGuard guard)
            => new ReportExecutor(_runner.Object, new QueryBuilder(), _repo.Object, guard);

        private static Questionnaire Minimal(Guid id, string specialtyCode) => new Questionnaire
        {
            Id = id,
            Title = "Cuestionario de prueba",
            SpecialtyCode = specialtyCode,
            Groups = new List<QuestionGroup>()
        };

        [Fact]
        public async Task ExecuteAsync_CuestionarioFueraDeScope_Lanza()
        {
            var id = Guid.NewGuid();
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(Minimal(id, "onco"));
            var sut = Sut(TestHelpers.GuardFor("cardio"));

            await Assert.ThrowsAsync<SpecialtyForbiddenException>(
                () => sut.ExecuteAsync(new ReportQuery { QuestionnaireId = id }));

            // Nunca debería haber llegado a ejecutar la consulta.
            _runner.Verify(r => r.RunAsync(It.IsAny<BuiltQuery>(), It.IsAny<int?>()), Times.Never);
        }

        [Fact]
        public async Task ExecuteAllAsync_CuestionarioFueraDeScope_Lanza()
        {
            var id = Guid.NewGuid();
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(Minimal(id, "onco"));
            var sut = Sut(TestHelpers.GuardFor("cardio"));

            await Assert.ThrowsAsync<SpecialtyForbiddenException>(
                () => sut.ExecuteAllAsync(new ReportQuery { QuestionnaireId = id }));
        }

        [Fact]
        public async Task ExecuteStatsAsync_CuestionarioFueraDeScope_Lanza()
        {
            var id = Guid.NewGuid();
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(Minimal(id, "onco"));
            var sut = Sut(TestHelpers.GuardFor("cardio"));

            await Assert.ThrowsAsync<SpecialtyForbiddenException>(
                () => sut.ExecuteStatsAsync(new ReportQuery { QuestionnaireId = id }));
        }

        [Fact]
        public async Task ExecuteAsync_CuestionarioDentroDeScope_Ejecuta()
        {
            var id = Guid.NewGuid();
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(Minimal(id, "cardio"));
            _runner.Setup(r => r.RunAsync(It.IsAny<BuiltQuery>(), It.IsAny<int?>()))
                .ReturnsAsync(new QueryRunResult { Rows = new List<Dictionary<string, object>>(), TotalResponses = 0 });
            var sut = Sut(TestHelpers.GuardFor("cardio"));

            var result = await sut.ExecuteAsync(new ReportQuery { QuestionnaireId = id });

            Assert.NotNull(result);
            _runner.Verify(r => r.RunAsync(It.IsAny<BuiltQuery>(), It.IsAny<int?>()), Times.Once);
        }

        [Fact]
        public async Task ExecuteAllAsync_PideAlAdaptadorQueNoLeaMasQueElTope_YRechazaSiSeSupera()
        {
            var id = Guid.NewGuid();
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(Minimal(id, "cardio"));
            _runner.Setup(r => r.RunAsync(It.IsAny<BuiltQuery>(), ReportExecutor.MaxSyncRows))
                .ReturnsAsync(new QueryRunResult { TotalResponses = ReportExecutor.MaxSyncRows + 1 });
            var sut = Sut(TestHelpers.GuardFor("cardio"));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => sut.ExecuteAllAsync(new ReportQuery { QuestionnaireId = id }));
        }

        [Fact]
        public async Task ExecuteAsync_Paginado_NoMandaTope()
        {
            var id = Guid.NewGuid();
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(Minimal(id, "cardio"));
            _runner.Setup(r => r.RunAsync(It.IsAny<BuiltQuery>(), It.IsAny<int?>()))
                .ReturnsAsync(new QueryRunResult());
            var sut = Sut(TestHelpers.GuardFor("cardio"));

            await sut.ExecuteAsync(new ReportQuery { QuestionnaireId = id });

            _runner.Verify(r => r.RunAsync(It.IsAny<BuiltQuery>(), null), Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_Administrador_EjecutaSinImportarLaEspecialidad()
        {
            var id = Guid.NewGuid();
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(Minimal(id, "onco"));
            _runner.Setup(r => r.RunAsync(It.IsAny<BuiltQuery>(), It.IsAny<int?>()))
                .ReturnsAsync(new QueryRunResult { Rows = new List<Dictionary<string, object>>(), TotalResponses = 0 });
            var sut = Sut(TestHelpers.UnrestrictedGuard());

            var result = await sut.ExecuteAsync(new ReportQuery { QuestionnaireId = id });

            Assert.NotNull(result);
        }
    }
}
