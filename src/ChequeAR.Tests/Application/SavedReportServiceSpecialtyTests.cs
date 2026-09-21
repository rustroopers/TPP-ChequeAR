using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ChequeAR.Application.Exceptions;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using Moq;
using Xunit;

namespace ChequeAR.Tests.Application
{
    /// <summary>
    /// Autorización por especialidad en SavedReportService (complementa
    /// SavedReportServiceTests, que sólo ejercita la regla de propietario con un guard sin
    /// restricción). Fija el fix de la firma: el scope de especialidad sale de
    /// ISpecialtyGuard, no de lo que un controller decida mandar.
    /// </summary>
    public class SavedReportServiceSpecialtyTests : IDisposable
    {
        private readonly Mock<ISavedReportRepository> _repo = new Mock<ISavedReportRepository>();
        private readonly AuditDispatcher _audit = TestHelpers.NoopAuditDispatcher();

        public void Dispose()
        {
            _audit.Dispose();
            GC.SuppressFinalize(this);
        }

        private SavedReportService Sut(ChequeAR.Application.Security.ISpecialtyGuard guard)
            => new SavedReportService(_repo.Object, _audit, guard);

        private static SavedReport Compartida(Guid id, string specialtyCode, string creadaPor = "otra.persona") => new SavedReport
        {
            Id = id, Name = "Cohorte", Scope = "specialty", SpecialtyCode = specialtyCode,
            CreatedBy = creadaPor, QuestionnaireId = Guid.NewGuid()
        };

        [Fact]
        public async Task ListForUserAsync_Restringido_PasaLosCodigosDelScope()
        {
            IEnumerable<string> capturado = new List<string> { "no-deberia-quedar" };
            _repo.Setup(r => r.ListForUserAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
                .Callback<string, IEnumerable<string>>((u, s) => capturado = s)
                .ReturnsAsync(new List<SavedReport>());
            var sut = Sut(TestHelpers.GuardFor("cardio", "neumo"));

            await sut.ListForUserAsync("dr.gomez");

            Assert.NotNull(capturado);
            Assert.Contains("cardio", capturado);
            Assert.Contains("neumo", capturado);
        }

        [Fact]
        public async Task ListForUserAsync_Unrestricted_PasaNull()
        {
            IEnumerable<string> capturado = new List<string> { "no-deberia-quedar" };
            _repo.Setup(r => r.ListForUserAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
                .Callback<string, IEnumerable<string>>((u, s) => capturado = s)
                .ReturnsAsync(new List<SavedReport>());
            var sut = Sut(TestHelpers.UnrestrictedGuard());

            await sut.ListForUserAsync("admin.demo");

            Assert.Null(capturado);
        }

        [Fact]
        public async Task GetForUserAsync_CompartidaFueraDeScope_NotFound()
        {
            var id = Guid.NewGuid();
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(Compartida(id, "onco"));
            var sut = Sut(TestHelpers.GuardFor("cardio"));

            var resultado = await sut.GetForUserAsync(id, "dr.gomez");

            Assert.Equal(SavedReportOutcome.NotFound, resultado.Outcome);
        }

        [Fact]
        public async Task GetForUserAsync_CompartidaDentroDeScope_Ok()
        {
            var id = Guid.NewGuid();
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(Compartida(id, "cardio"));
            var sut = Sut(TestHelpers.GuardFor("cardio"));

            var resultado = await sut.GetForUserAsync(id, "dr.gomez");

            Assert.Equal(SavedReportOutcome.Ok, resultado.Outcome);
        }

        [Fact]
        public async Task GetForUserAsync_Propia_IgnoraElScope()
        {
            var id = Guid.NewGuid();
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(Compartida(id, "onco", creadaPor: "dr.gomez"));
            var sut = Sut(TestHelpers.GuardFor("cardio"));

            var resultado = await sut.GetForUserAsync(id, "dr.gomez");

            Assert.Equal(SavedReportOutcome.Ok, resultado.Outcome);
        }

        [Fact]
        public async Task CreateAsync_EspecialidadFueraDeScope_Lanza()
        {
            var sut = Sut(TestHelpers.GuardFor("cardio"));
            var nueva = new SavedReport { Name = "x", Scope = "specialty", SpecialtyCode = "onco" };

            await Assert.ThrowsAsync<SpecialtyForbiddenException>(() => sut.CreateAsync(nueva, "dr.gomez"));
            _repo.Verify(r => r.AddAsync(It.IsAny<SavedReport>()), Times.Never);
        }

        [Fact]
        public async Task CreateAsync_EspecialidadDentroDeScope_Permite()
        {
            var sut = Sut(TestHelpers.GuardFor("cardio"));
            var nueva = new SavedReport { Name = "x", Scope = "specialty", SpecialtyCode = "cardio" };

            await sut.CreateAsync(nueva, "dr.gomez");

            _repo.Verify(r => r.AddAsync(It.IsAny<SavedReport>()), Times.Once);
        }

        [Fact]
        public async Task CreateAsync_SinEspecialidad_NoValidaContraElScope()
        {
            // Scope="private" no tiene SpecialtyCode: nada que autorizar por especialidad.
            var sut = Sut(TestHelpers.GuardFor("cardio"));
            var nueva = new SavedReport { Name = "x", Scope = "private", SpecialtyCode = null };

            await sut.CreateAsync(nueva, "dr.gomez");

            _repo.Verify(r => r.AddAsync(It.IsAny<SavedReport>()), Times.Once);
        }

        [Fact]
        public async Task UpdateAsync_CambiaAEspecialidadFueraDeScope_Lanza()
        {
            var id = Guid.NewGuid();
            var existente = Compartida(id, "cardio", creadaPor: "dr.gomez");
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(existente);
            var sut = Sut(TestHelpers.GuardFor("cardio"));

            await Assert.ThrowsAsync<SpecialtyForbiddenException>(
                () => sut.UpdateAsync(id, new SavedReport { SpecialtyCode = "onco" }, "dr.gomez"));
        }
    }
}
