using System;
using System.Collections.Generic;
using System.Linq;
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
    public class SavedReportServiceTests : IDisposable
    {
        private readonly Mock<ISavedReportRepository> _repo = new Mock<ISavedReportRepository>();
        private readonly Mock<IAuditRepository> _auditRepo = new Mock<IAuditRepository>();
        private readonly AuditDispatcher _audit;
        private readonly SavedReportService _sut;

        public SavedReportServiceTests()
        {
            _auditRepo.Setup(r => r.AddManyAsync(It.IsAny<IEnumerable<AuditEvent>>())).Returns(Task.CompletedTask);
            // Intervalo de flush largo: los eventos quedan en la cola y se drenan a mano con
            // FlushAsync en la prueba que lo necesita, sin que el timer dispare durante el test.
            _audit = new AuditDispatcher(_auditRepo.Object, flushIntervalMs: 600000);
            _sut = new SavedReportService(_repo.Object, _audit, TestHelpers.UnrestrictedGuard());
        }

        // xUnit llama Dispose() despues de cada test: sin esto, el Timer interno de
        // AuditDispatcher quedaba vivo (10 min de intervalo) por cada instancia de esta clase.
        public void Dispose()
        {
            _audit.Dispose();
            GC.SuppressFinalize(this);
        }

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

        [Fact]
        public async Task Crear_RegistraEventoDeAuditoria_ConFotoDeLaConsulta()
        {
            IEnumerable<AuditEvent> emitidos = null;
            _auditRepo.Setup(r => r.AddManyAsync(It.IsAny<IEnumerable<AuditEvent>>()))
                      .Callback<IEnumerable<AuditEvent>>(b => emitidos = b.ToList())
                      .Returns(Task.CompletedTask);

            var creada = await _sut.CreateAsync(
                new SavedReport { Name = "Cohorte cardio", QueryJson = "{\"filters\":{}}" }, "dr.gomez");
            await _audit.FlushAsync();

            Assert.NotNull(emitidos);
            var evt = Assert.Single(emitidos);
            Assert.Equal("C", evt.Action);                          // create
            Assert.Equal("SavedReport/" + creada.Id, evt.EntityRef);
            Assert.Equal("dr.gomez", evt.AgentUserId);
            var snap = Newtonsoft.Json.JsonConvert.DeserializeObject<SavedReportAuditSnapshot>(evt.FhirJson);
            Assert.Equal("Cohorte cardio", snap.Name);              // el detalle podrá mostrar el nombre
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
