using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Dtos;
using ChequeAR.Application.Exceptions;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using Moq;
using Xunit;

namespace ChequeAR.Tests.Application
{
    public class PatientTrendServiceTests
    {
        private readonly Mock<IQuestionnaireRepository> _qRepo = new Mock<IQuestionnaireRepository>();
        private readonly Mock<IPatientTrendRepository> _trends = new Mock<IPatientTrendRepository>();
        private readonly PatientTrendService _sut;

        public PatientTrendServiceTests()
        {
            _sut = new PatientTrendService(_qRepo.Object, _trends.Object, TestHelpers.UnrestrictedGuard());
        }

        // --- Scope de especialidad -----------------------------------------------------

        [Fact]
        public async Task GetSelectableParameters_CuestionarioFueraDeScope_Lanza()
        {
            var id = Guid.NewGuid();
            _qRepo.Setup(r => r.GetAsync(id)).ReturnsAsync(new Questionnaire { Id = id, SpecialtyCode = "onco" });
            var sut = new PatientTrendService(_qRepo.Object, _trends.Object, TestHelpers.GuardFor("cardio"));

            await Assert.ThrowsAsync<SpecialtyForbiddenException>(() => sut.GetSelectableParametersAsync(id));
        }

        [Fact]
        public async Task GetTrend_CuestionarioFueraDeScope_Lanza_YNoConsultaLasSeries()
        {
            var id = Guid.NewGuid();
            _qRepo.Setup(r => r.GetAsync(id)).ReturnsAsync(new Questionnaire { Id = id, SpecialtyCode = "onco" });
            var sut = new PatientTrendService(_qRepo.Object, _trends.Object, TestHelpers.GuardFor("cardio"));

            await Assert.ThrowsAsync<SpecialtyForbiddenException>(() => sut.GetTrendAsync("HC-1", id, new[] { "peso" }));

            _trends.Verify(t => t.GetSeriesAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<IEnumerable<string>>()), Times.Never);
        }

        // --- Selección de parámetros ---------------------------------------------------

        // Solo numéricas y de fecha, no repetibles: texto, choice, boolean, repetibles quedan afuera.
        [Fact]
        public async Task GetSelectableParameters_KeepsOnlyGraphableNonRepeatingQuestions()
        {
            var id = Guid.NewGuid();
            _qRepo.Setup(r => r.GetAsync(id)).ReturnsAsync(WithQuestions(id,
                Q("peso", QuestionType.Decimal),
                Q("edad", QuestionType.Integer),
                Q("control", QuestionType.Date),
                Q("nombre", QuestionType.String),
                Q("dx", QuestionType.Choice),
                Q("fuma", QuestionType.Boolean),
                Q("sintomas", QuestionType.Decimal, repeats: true)));

            var result = await _sut.GetSelectableParametersAsync(id);

            var linkIds = result.Parameters.Select(p => p.LinkId).ToList();
            Assert.Equal(new[] { "peso", "edad", "control" }, linkIds);
            Assert.DoesNotContain("sintomas", linkIds);   // graficable por tipo pero repetible
        }

        [Fact]
        public async Task GetSelectableParameters_SerializesTypeInLowercase()
        {
            var id = Guid.NewGuid();
            _qRepo.Setup(r => r.GetAsync(id)).ReturnsAsync(WithQuestions(id,
                Q("peso", QuestionType.Decimal), Q("edad", QuestionType.Integer), Q("control", QuestionType.Date)));

            var result = await _sut.GetSelectableParametersAsync(id);

            Assert.Equal("decimal", result.Parameters.Single(p => p.LinkId == "peso").Type);
            Assert.Equal("integer", result.Parameters.Single(p => p.LinkId == "edad").Type);
            Assert.Equal("date", result.Parameters.Single(p => p.LinkId == "control").Type);
        }

        [Fact]
        public async Task GetSelectableParameters_QuestionnaireNotFound_ReturnsNull()
        {
            var id = Guid.NewGuid();
            _qRepo.Setup(r => r.GetAsync(id)).ReturnsAsync((Questionnaire)null);

            Assert.Null(await _sut.GetSelectableParametersAsync(id));
        }

        [Fact]
        public async Task GetSelectableParameters_NoGraphableQuestions_ReturnsEmptyList()
        {
            var id = Guid.NewGuid();
            _qRepo.Setup(r => r.GetAsync(id)).ReturnsAsync(WithQuestions(id,
                Q("nombre", QuestionType.String), Q("dx", QuestionType.Choice)));

            var result = await _sut.GetSelectableParametersAsync(id);

            Assert.NotNull(result);
            Assert.Empty(result.Parameters);
        }

        // --- Rango normal / unidad -----------------------------------------------------

        [Fact]
        public async Task GetSelectableParameters_ReadsNormalRangeAndUnitFromValidationJson()
        {
            var id = Guid.NewGuid();
            _qRepo.Setup(r => r.GetAsync(id)).ReturnsAsync(WithQuestions(id,
                Q("peso", QuestionType.Decimal, validation: "{\"min\":2.5,\"max\":120,\"unit\":\"kg\"}")));

            var p = (await _sut.GetSelectableParametersAsync(id)).Parameters.Single();

            Assert.Equal("kg", p.Unit);
            Assert.NotNull(p.NormalRange);
            Assert.Equal(2.5m, p.NormalRange.Min);
            Assert.Equal(120m, p.NormalRange.Max);
        }

        [Fact]
        public async Task GetSelectableParameters_OneSidedRange_KeepsOnlyThePresentBound()
        {
            var id = Guid.NewGuid();
            _qRepo.Setup(r => r.GetAsync(id)).ReturnsAsync(WithQuestions(id,
                Q("plaquetas", QuestionType.Integer, validation: "{\"min\":150000}")));

            var p = (await _sut.GetSelectableParametersAsync(id)).Parameters.Single();

            Assert.NotNull(p.NormalRange);
            Assert.Equal(150000m, p.NormalRange.Min);
            Assert.Null(p.NormalRange.Max);
        }

        [Fact]
        public async Task GetSelectableParameters_NoValidation_HasNoRangeNorUnit()
        {
            var id = Guid.NewGuid();
            _qRepo.Setup(r => r.GetAsync(id)).ReturnsAsync(WithQuestions(id,
                Q("peso", QuestionType.Decimal, validation: null)));

            var p = (await _sut.GetSelectableParametersAsync(id)).Parameters.Single();

            Assert.Null(p.NormalRange);
            Assert.Null(p.Unit);
        }

        // JSON corrupto: se muestra el parámetro igual, sin rango ni unidad — nunca rompe.
        [Fact]
        public async Task GetSelectableParameters_CorruptValidationJson_StillListsParameterWithoutRange()
        {
            var id = Guid.NewGuid();
            _qRepo.Setup(r => r.GetAsync(id)).ReturnsAsync(WithQuestions(id,
                Q("peso", QuestionType.Decimal, validation: "{ esto no es json")));

            var result = await _sut.GetSelectableParametersAsync(id);

            var p = Assert.Single(result.Parameters);
            Assert.Equal("peso", p.LinkId);
            Assert.Null(p.NormalRange);
            Assert.Null(p.Unit);
        }

        // --- Armado de series ----------------------------------------------------------

        [Fact]
        public async Task GetTrend_EnrichesSeriesWithMetadataFromQuestionnaire()
        {
            var id = Guid.NewGuid();
            _qRepo.Setup(r => r.GetAsync(id)).ReturnsAsync(WithQuestions(id,
                Q("peso", QuestionType.Decimal, validation: "{\"min\":2.5,\"max\":120,\"unit\":\"kg\"}", text: "Peso")));
            _trends.Setup(t => t.GetSeriesAsync("PAC-1", id, It.IsAny<IEnumerable<string>>()))
                   .ReturnsAsync(new List<TrendSeriesDto>
                   {
                       new TrendSeriesDto { LinkId = "peso", Points = { new TrendPointDto { Value = 14.2m } } }
                   });

            var series = await _sut.GetTrendAsync("PAC-1", id, new[] { "peso" });

            var s = Assert.Single(series);
            Assert.Equal("Peso", s.Text);
            Assert.Equal("decimal", s.Type);
            Assert.Equal("kg", s.Unit);
            Assert.Equal(120m, s.NormalRange.Max);
            Assert.Single(s.Points);
        }

        // Un linkId válido sin datos aparece igual, con Points vacío (no un error, no un 404).
        [Fact]
        public async Task GetTrend_ValidLinkWithoutData_ReturnsEmptyPointsNotMissing()
        {
            var id = Guid.NewGuid();
            _qRepo.Setup(r => r.GetAsync(id)).ReturnsAsync(WithQuestions(id, Q("lab", QuestionType.Decimal)));
            _trends.Setup(t => t.GetSeriesAsync(It.IsAny<string>(), id, It.IsAny<IEnumerable<string>>()))
                   .ReturnsAsync(new List<TrendSeriesDto>());   // el repo no trajo nada

            var series = await _sut.GetTrendAsync("PAC-1", id, new[] { "lab" });

            var s = Assert.Single(series);
            Assert.Equal("lab", s.LinkId);
            Assert.NotNull(s.Points);
            Assert.Empty(s.Points);
        }

        // linkIds que no pertenecen al cuestionario, o no son graficables, se descartan sin
        // afectar al resto de la respuesta.
        [Fact]
        public async Task GetTrend_IgnoresUnknownAndNonGraphableLinkIds()
        {
            var id = Guid.NewGuid();
            _qRepo.Setup(r => r.GetAsync(id)).ReturnsAsync(WithQuestions(id,
                Q("peso", QuestionType.Decimal), Q("nombre", QuestionType.String)));

            IEnumerable<string> asked = null;
            _trends.Setup(t => t.GetSeriesAsync("PAC-1", id, It.IsAny<IEnumerable<string>>()))
                   .Callback<string, Guid, IEnumerable<string>>((_, __, links) => asked = links.ToList())
                   .ReturnsAsync(new List<TrendSeriesDto> { new TrendSeriesDto { LinkId = "peso" } });

            var series = await _sut.GetTrendAsync("PAC-1", id, new[] { "peso", "nombre", "no-existe" });

            Assert.Equal(new[] { "peso" }, asked);                  // solo el graficable llega al repo
            Assert.Equal(new[] { "peso" }, series.Select(s => s.LinkId));
        }

        [Fact]
        public async Task GetTrend_NoLinkIds_ReturnsEmptyWithoutHittingRepo()
        {
            var id = Guid.NewGuid();

            var series = await _sut.GetTrendAsync("PAC-1", id, Array.Empty<string>());

            Assert.Empty(series);
            _trends.Verify(t => t.GetSeriesAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<IEnumerable<string>>()), Times.Never);
        }

        [Fact]
        public async Task GetTrend_QuestionnaireNotFound_ReturnsEmpty()
        {
            var id = Guid.NewGuid();
            _qRepo.Setup(r => r.GetAsync(id)).ReturnsAsync((Questionnaire)null);

            var series = await _sut.GetTrendAsync("PAC-1", id, new[] { "peso" });

            Assert.Empty(series);
        }

        // --- helpers -------------------------------------------------------------------

        private static Questionnaire WithQuestions(Guid id, params Question[] questions)
        {
            var q = new Questionnaire { Id = id, Status = QuestionnaireStatus.Active };
            var order = 0;
            foreach (var question in questions) { question.OrderIndex = order++; q.Questions.Add(question); }
            return q;
        }

        private static Question Q(string linkId, QuestionType type, bool repeats = false, string validation = null, string text = null)
        {
            return new Question
            {
                LinkId = linkId,
                Text = text ?? linkId,
                Type = type,
                RepeatsAllowed = repeats,
                ValidationJson = validation
            };
        }
    }
}
