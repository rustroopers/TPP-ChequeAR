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
    public class AuditDispatcherSpillTests
    {
        private const int SinTimer = 600000;

        private static AuditEvent Evento() => new AuditEvent
        {
            Id = Guid.NewGuid(), Recorded = DateTime.UtcNow, Action = "R",
            EntityRef = "Patient/1", AgentUserId = "m1", FhirJson = "{\"detalle\":true}"
        };

        private static Mock<IAuditRepository> BaseCaida()
        {
            var repo = new Mock<IAuditRepository>();
            repo.Setup(r => r.AddManyAsync(It.IsAny<IEnumerable<AuditEvent>>())).ThrowsAsync(new InvalidOperationException("ORA-12541"));
            repo.Setup(r => r.AddAsync(It.IsAny<AuditEvent>())).ThrowsAsync(new InvalidOperationException("ORA-12541"));
            return repo;
        }

        [Fact]
        public async Task TrasMaxAttempts_ElEventoCompletoVaASpill()
        {
            var derramados = new List<AuditEvent>();
            var repo = BaseCaida();
            var sut = new AuditDispatcher(() => repo.Object, SinTimer, (ex, msg) => { }) { Spill = derramados.Add };
            var evt = Evento();
            sut.Enqueue(evt);

            for (var i = 0; i < AuditDispatcher.MaxAttempts + 2; i++) await sut.FlushAsync();

            Assert.Same(evt, Assert.Single(derramados));
        }

        [Fact]
        public void ConLaColaLlena_LosNuevosVanASpill()
        {
            var derramados = new List<AuditEvent>();
            var errores = new List<string>();
            var sut = new AuditDispatcher(() => BaseCaida().Object, SinTimer, (ex, msg) => errores.Add(msg)) { Spill = derramados.Add };

            for (var i = 0; i < AuditDispatcher.MaxQueued + 3; i++) sut.Enqueue(Evento());

            Assert.Equal(3, derramados.Count);
            Assert.Single(errores, m => m.Contains("Cola de auditoría llena"));
        }

        [Fact]
        public void AlApagar_LoQueQuedaVaASpill()
        {
            var derramados = new List<AuditEvent>();
            var sut = new AuditDispatcher(() => BaseCaida().Object, SinTimer, (ex, msg) => { }) { Spill = derramados.Add };
            sut.Enqueue(Evento());
            sut.Enqueue(Evento());

            sut.Dispose();

            Assert.Equal(2, derramados.Count);
        }

        [Fact]
        public async Task SiSpillFalla_SeReportaYNoLanza()
        {
            var errores = new List<string>();
            var repo = BaseCaida();
            var sut = new AuditDispatcher(() => repo.Object, SinTimer, (ex, msg) => errores.Add(msg))
            {
                Spill = _ => throw new System.IO.IOException("disco lleno")
            };
            sut.Enqueue(Evento());

            for (var i = 0; i < AuditDispatcher.MaxAttempts + 2; i++) await sut.FlushAsync();

            Assert.Contains(errores, m => m.Contains("perdido"));
        }
    }
}
