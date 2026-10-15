using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Security;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using Moq;
using Xunit;

namespace ChequeAR.Tests.Application
{
    /// <summary>
    /// Búsqueda de pacientes cuando las vistas del hospital no responden: se arma con las
    /// fichas propias. Filtra antes de resolver nada caro, y la especialidad sale de una sola
    /// columna, no del cuestionario completo.
    /// </summary>
    public class PatientServiceDegradedSearchTests
    {
        private readonly Mock<IOracleViewClient> _oracle = new Mock<IOracleViewClient>();
        private readonly Mock<IQuestionnaireResponseRepository> _responses = new Mock<IQuestionnaireResponseRepository>();
        private readonly Mock<IQuestionnaireRepository> _questionnaires = new Mock<IQuestionnaireRepository>();
        private readonly Guid _cardio = Guid.NewGuid();
        private readonly PatientService _sut;

        public PatientServiceDegradedSearchTests()
        {
            _oracle.Setup(o => o.SearchAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<int>()))
                   .ThrowsAsync(new InvalidOperationException("hospital caído"));
            _questionnaires.Setup(q => q.GetSpecialtyCodeAsync(_cardio)).ReturnsAsync("cardio");
            _responses.Setup(r => r.SearchAsync(null, null, null, 0, It.IsAny<int>(), It.IsAny<SpecialtyScope>()))
                      .ReturnsAsync(new List<QuestionnaireResponse>
                      {
                          Ficha("PAC-1", "Gómez, Ana"),
                          Ficha("PAC-1", "Gómez, Ana"),
                          Ficha("PAC-2", "Pérez, Luis"),
                          Ficha("PAC-3", "Gómez, Juan")
                      });
            _sut = new PatientService(Mock.Of<IHospitalFhirClient>(), _oracle.Object, _responses.Object,
                _questionnaires.Object, TestHelpers.CatalogWith(), TestHelpers.UnrestrictedGuard());
        }

        [Fact]
        public async Task FiltraPorNombre_UnResultadoPorPaciente()
        {
            var hits = await _sut.SearchAsync("gómez");

            Assert.Equal(new[] { "PAC-1", "PAC-3" }, hits.Select(h => h.HospitalId).OrderBy(x => x));
            Assert.All(hits, h => Assert.Equal("Cardiología", h.SpecialtyLabel));
        }

        [Fact]
        public async Task FiltraPorHistoriaClinica()
        {
            var hits = await _sut.SearchAsync("pac-2");

            Assert.Equal("Pérez, Luis", hits.Single().FullName);
        }

        [Fact]
        public async Task ResuelveLaEspecialidadUnaVezPorCuestionario_SinCargarElAgregado()
        {
            await _sut.SearchAsync("gómez");

            _questionnaires.Verify(q => q.GetSpecialtyCodeAsync(_cardio), Times.Once);
            _questionnaires.Verify(q => q.GetAsync(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task SinCoincidencias_NoResuelveNada()
        {
            Assert.Empty(await _sut.SearchAsync("zzz"));
            _questionnaires.Verify(q => q.GetSpecialtyCodeAsync(It.IsAny<Guid>()), Times.Never);
        }

        private QuestionnaireResponse Ficha(string hc, string nombre) => new QuestionnaireResponse
        {
            Id = Guid.NewGuid(),
            QuestionnaireId = _cardio,
            PatientHospitalId = hc,
            AuthoredAt = DateTime.UtcNow,
            FhirJson = "{\"resourceType\":\"QuestionnaireResponse\",\"contained\":[{\"resourceType\":\"Patient\",\"name\":[{\"text\":\"" + nombre + "\"}]}]}"
        };
    }
}
