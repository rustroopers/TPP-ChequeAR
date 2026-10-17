using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ChequeAR.Application.Dtos;
using ChequeAR.Application.Exceptions;
using ChequeAR.Application.Fhir;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Reports;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
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

        // Estadísticas sobre la estructura publicada, con la metadata que necesita el export.
        [Fact]
        public async Task ExecuteStatsAsync_CalculaSobreLaCohorteYDescribeLaConsulta()
        {
            var id = Guid.NewGuid();
            var q = Minimal(id, "cardio");
            var foto = new Questionnaire { Id = id, Title = q.Title };
            foto.Questions.Add(new Question { LinkId = "peso", Text = "Peso", Type = QuestionType.Decimal });
            var v = new QuestionnaireVersion { Id = Guid.NewGuid(), QuestionnaireId = id, VersionNumber = 1, FhirJson = FhirQuestionnaireBuilder.Build(foto, 1) };
            q.Versions.Add(v);
            q.CurrentVersionId = v.Id;
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(q);
            _runner.Setup(r => r.RunAsync(It.IsAny<BuiltQuery>(), It.IsAny<int?>())).ReturnsAsync(new QueryRunResult
            {
                Rows = new List<Dictionary<string, object>>
                {
                    new Dictionary<string, object> { ["ResponseId"] = "a", ["LinkId"] = "peso", ["ValueDecimal"] = 20m },
                    new Dictionary<string, object> { ["ResponseId"] = "b", ["LinkId"] = "peso", ["ValueDecimal"] = 30m }
                },
                TotalResponses = 3
            });
            var desde = new DateTime(2026, 10, 1);

            var stats = await Sut(TestHelpers.GuardFor("cardio")).ExecuteStatsAsync(new ReportQuery
            {
                QuestionnaireId = id,
                DateRange = new DateRange { From = desde }
            });

            Assert.Equal(3, stats.Responses);
            var peso = Assert.Single(stats.Questions);
            Assert.Equal(2, peso.Answered);
            Assert.Equal(25m, peso.Mean);
            Assert.Equal("Cuestionario de prueba", stats.QuestionnaireTitle);
            Assert.Equal(desde, stats.DateFrom);
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

            await Assert.ThrowsAsync<BusinessRuleException>(
                () => sut.ExecuteAllAsync(new ReportQuery { QuestionnaireId = id }));
        }

        [Fact]
        public async Task CuestionarioInexistente_LanzaNotFound()
        {
            var id = Guid.NewGuid();
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync((Questionnaire)null);

            await Assert.ThrowsAsync<NotFoundException>(
                () => Sut(TestHelpers.UnrestrictedGuard()).ExecuteAsync(new ReportQuery { QuestionnaireId = id }));
        }

        [Fact]
        public async Task FiltroMalArmado_LanzaValidacion_YNoEjecuta()
        {
            var id = Guid.NewGuid();
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(Minimal(id, "cardio"));
            var query = new ReportQuery
            {
                QuestionnaireId = id,
                Filters = new FilterNode
                {
                    Predicates = new List<FilterPredicate> { new FilterPredicate { LinkId = "no-existe", Op = "=", Value = "x" } }
                }
            };

            await Assert.ThrowsAsync<ValidationFailedException>(
                () => Sut(TestHelpers.UnrestrictedGuard()).ExecuteAsync(query));
            _runner.Verify(r => r.RunAsync(It.IsAny<BuiltQuery>(), It.IsAny<int?>()), Times.Never);
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
