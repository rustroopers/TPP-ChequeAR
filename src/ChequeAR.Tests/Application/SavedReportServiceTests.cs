using System;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using Moq;
using Xunit;

namespace ChequeAR.Tests.Application
{
    /// <summary>
    /// Autorización por propietario de las consultas guardadas.
    ///
    /// La regla existía desde antes, pero vivía dentro del controller y por eso NO tenía
    /// ninguna prueba: verificarla exigía levantar la capa web. Moverla a la capa de negocio
    /// es lo que la vuelve verificable — que es exactamente lo que afirma el documento de
    /// arquitectura sobre testing independiente.
    /// </summary>
    public class SavedReportServiceTests
    {
        private readonly Mock<ISavedReportRepository> _repo = new Mock<ISavedReportRepository>();
        private readonly SavedReportService _sut;

        public SavedReportServiceTests() { _sut = new SavedReportService(_repo.Object); }

        [Fact]
        public async Task Actualizar_DeOtraPersona_SeRechaza_YNoTocaLaBase()
        {
            var id = Guid.NewGuid();
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(Consulta(id, creadaPor: "dra.lopez"));

            var resultado = await _sut.UpdateAsync(id, new SavedReport { Name = "robada" }, "dr.gomez");

            Assert.Equal(SavedReportOutcome.Forbidden, resultado.Outcome);
            _repo.Verify(r => r.UpdateAsync(It.IsAny<SavedReport>()), Times.Never);
        }

        [Fact]
        public async Task Eliminar_DeOtraPersona_SeRechaza_YNoTocaLaBase()
        {
            var id = Guid.NewGuid();
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(Consulta(id, creadaPor: "dra.lopez"));

            var resultado = await _sut.DeleteAsync(id, "dr.gomez");

            Assert.Equal(SavedReportOutcome.Forbidden, resultado.Outcome);
            _repo.Verify(r => r.DeleteAsync(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task Actualizar_LaPropia_Funciona()
        {
            var id = Guid.NewGuid();
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(Consulta(id, creadaPor: "dr.gomez"));

            var resultado = await _sut.UpdateAsync(id, new SavedReport { Name = "nuevo nombre" }, "dr.gomez");

            Assert.Equal(SavedReportOutcome.Ok, resultado.Outcome);
            Assert.Equal("nuevo nombre", resultado.Report.Name);
            _repo.Verify(r => r.UpdateAsync(It.IsAny<SavedReport>()), Times.Once);
        }

        /// <summary>
        /// Lo que el cliente no manda se conserva. Un reemplazo completo convertiría cualquier
        /// omisión en un borrado silencioso de la consulta que alguien armó.
        /// </summary>
        [Fact]
        public async Task Actualizar_ConservaLosCamposQueNoVienen()
        {
            var id = Guid.NewGuid();
            var existente = Consulta(id, creadaPor: "dr.gomez");
            existente.QueryJson = "{\"filtros\":\"originales\"}";
            existente.SpecialtyCode = "cardio";
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(existente);

            var resultado = await _sut.UpdateAsync(id, new SavedReport { Name = "sólo el nombre" }, "dr.gomez");

            Assert.Equal("{\"filtros\":\"originales\"}", resultado.Report.QueryJson);
            Assert.Equal("cardio", resultado.Report.SpecialtyCode);
        }

        [Fact]
        public async Task Inexistente_SeDistingueDeSinPermiso()
        {
            // No es lo mismo 404 que 403: confundirlos filtra la existencia de recursos ajenos.
            var id = Guid.NewGuid();
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync((SavedReport)null);

            Assert.Equal(SavedReportOutcome.NotFound, (await _sut.UpdateAsync(id, new SavedReport(), "x")).Outcome);
            Assert.Equal(SavedReportOutcome.NotFound, (await _sut.DeleteAsync(id, "x")).Outcome);
        }

        /// <summary>
        /// Que una consulta sea visible para toda una especialidad no la vuelve editable por
        /// cualquiera: visibilidad y edición son permisos distintos.
        /// </summary>
        [Fact]
        public async Task AlcanceDeEspecialidad_NoHabilitaAEditarla()
        {
            var id = Guid.NewGuid();
            var compartida = Consulta(id, creadaPor: "dra.lopez");
            compartida.Scope = "specialty";
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(compartida);

            var resultado = await _sut.UpdateAsync(id, new SavedReport(), "dr.gomez");

            Assert.Equal(SavedReportOutcome.Forbidden, resultado.Outcome);
        }

        [Fact]
        public async Task Crear_AsignaPropietarioYFechas()
        {
            var creada = await _sut.CreateAsync(new SavedReport { Name = "mía" }, "dr.gomez");

            Assert.NotEqual(Guid.Empty, creada.Id);
            Assert.Equal("dr.gomez", creada.CreatedBy);
            Assert.NotEqual(default(DateTime), creada.CreatedAt);
            _repo.Verify(r => r.AddAsync(It.IsAny<SavedReport>()), Times.Once);
        }

        private static SavedReport Consulta(Guid id, string creadaPor) => new SavedReport
        {
            Id = id,
            Name = "Cohorte de control",
            CreatedBy = creadaPor,
            QuestionnaireId = Guid.NewGuid()
        };
    }
}
