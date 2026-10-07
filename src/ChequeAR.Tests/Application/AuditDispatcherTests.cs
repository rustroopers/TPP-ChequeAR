using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using Moq;
using Xunit;

namespace ChequeAR.Tests.Application
{
    /// <summary>
    /// El dispatcher es un singleton que corre en el timer: un fallo de la base no puede
    /// escaparse (terminaría el proceso), ni perder eventos en silencio, ni reutilizar un
    /// DbContext entre hilos.
    /// </summary>
    public class AuditDispatcherTests
    {
        private const int SinTimer = 600000;

        [Fact]
        public async Task Flush_PideUnRepositorioNuevoPorOperacion_YLoLibera()
        {
            var creados = new List<RepoDescartable>();
            using (var sut = new AuditDispatcher(() => { var r = new RepoDescartable(); creados.Add(r); return r; }, SinTimer))
            {
                sut.Enqueue(Evento());
                await sut.FlushAsync();
                await sut.PersistImmediatelyAsync(Evento());

                Assert.Equal(2, creados.Count);
                Assert.All(creados, r => Assert.True(r.Disposed));
            }
        }

        [Fact]
        public async Task Flush_SiLaBaseFalla_NoLanza_YElLoteVuelveALaCola()
        {
            var repo = new Mock<IAuditRepository>();
            var errores = new List<string>();
            repo.SetupSequence(r => r.AddManyAsync(It.IsAny<IEnumerable<AuditEvent>>()))
                .ThrowsAsync(new InvalidOperationException("ORA-12541"))
                .Returns(Task.CompletedTask);

            var sut = new AuditDispatcher(() => repo.Object, SinTimer, (ex, msg) => errores.Add(msg));
            var evt = Evento();
            sut.Enqueue(evt);

            await sut.FlushAsync();   // falla: no lanza
            await sut.FlushAsync();   // reintento

            Assert.NotEmpty(errores);
            repo.Verify(r => r.AddManyAsync(It.Is<IEnumerable<AuditEvent>>(es => es.Single() == evt)), Times.Exactly(2));
        }

        [Fact]
        public async Task Flush_DescartaElEventoTrasMaxAttempts_YLoReporta()
        {
            var repo = new Mock<IAuditRepository>();
            repo.Setup(r => r.AddManyAsync(It.IsAny<IEnumerable<AuditEvent>>()))
                .ThrowsAsync(new InvalidOperationException("dato inválido"));
            var errores = new List<string>();

            var sut = new AuditDispatcher(() => repo.Object, SinTimer, (ex, msg) => errores.Add(msg));
            sut.Enqueue(Evento());

            for (var i = 0; i < AuditDispatcher.MaxAttempts + 2; i++) await sut.FlushAsync();

            repo.Verify(r => r.AddManyAsync(It.IsAny<IEnumerable<AuditEvent>>()), Times.Exactly(AuditDispatcher.MaxAttempts));
            Assert.Contains(errores, m => m.Contains("descartado"));
        }

        // Un evento inválido no puede arrastrar al resto del lote ni gastarles intentos.
        [Fact]
        public async Task Flush_UnEventoInvalido_NoArrastraAlRestoDelLote()
        {
            var malo = Evento();
            var buenos = new[] { Evento(), Evento() };
            var guardados = new List<AuditEvent>();
            var repo = new Mock<IAuditRepository>();
            repo.Setup(r => r.AddManyAsync(It.IsAny<IEnumerable<AuditEvent>>()))
                .ThrowsAsync(new InvalidOperationException("ORA-12899"));
            repo.Setup(r => r.AddAsync(malo)).ThrowsAsync(new InvalidOperationException("ORA-12899"));
            repo.Setup(r => r.AddAsync(It.Is<AuditEvent>(e => e != malo)))
                .Returns(Task.CompletedTask).Callback<AuditEvent>(guardados.Add);

            var sut = new AuditDispatcher(() => repo.Object, SinTimer, (ex, msg) => { });
            sut.Enqueue(buenos[0]);
            sut.Enqueue(malo);
            sut.Enqueue(buenos[1]);

            await sut.FlushAsync();

            Assert.Equal(buenos, guardados);
        }

        // Con la base caída no se prueba evento por evento: cada uno esperaría su timeout.
        [Fact]
        public async Task Flush_ConLaBaseCaida_CortaElReintentoIndividualEnseguida()
        {
            var repo = new Mock<IAuditRepository>();
            repo.Setup(r => r.AddManyAsync(It.IsAny<IEnumerable<AuditEvent>>()))
                .ThrowsAsync(new InvalidOperationException("ORA-12541"));
            repo.Setup(r => r.AddAsync(It.IsAny<AuditEvent>())).ThrowsAsync(new InvalidOperationException("ORA-12541"));

            var sut = new AuditDispatcher(() => repo.Object, SinTimer, (ex, msg) => { });
            for (var i = 0; i < 50; i++) sut.Enqueue(Evento());

            await sut.FlushAsync();

            repo.Verify(r => r.AddAsync(It.IsAny<AuditEvent>()), Times.Exactly(3));
        }

        [Fact]
        public async Task PersistImmediately_SiLaBaseFalla_NoLanza_YLoEncolaParaElFlush()
        {
            var repo = new Mock<IAuditRepository>();
            repo.Setup(r => r.AddAsync(It.IsAny<AuditEvent>())).ThrowsAsync(new InvalidOperationException("caída"));
            repo.Setup(r => r.AddManyAsync(It.IsAny<IEnumerable<AuditEvent>>())).Returns(Task.CompletedTask);

            var sut = new AuditDispatcher(() => repo.Object, SinTimer);
            var evt = Evento();

            await sut.PersistImmediatelyAsync(evt);
            await sut.FlushAsync();

            repo.Verify(r => r.AddManyAsync(It.Is<IEnumerable<AuditEvent>>(es => es.Contains(evt))), Times.Once);
        }

        [Fact]
        public void Timer_UnFalloDeLaBaseNoEscapaDelCallback()
        {
            var repo = new Mock<IAuditRepository>();
            var reportado = new ManualResetEventSlim();
            repo.Setup(r => r.AddManyAsync(It.IsAny<IEnumerable<AuditEvent>>()))
                .ThrowsAsync(new InvalidOperationException("ORA-03113"));

            // Si la excepción escapara del callback, xunit vería caer el proceso de test.
            using (var sut = new AuditDispatcher(() => repo.Object, 20, (ex, msg) => reportado.Set()))
            {
                sut.Enqueue(Evento());
                Assert.True(reportado.Wait(TimeSpan.FromSeconds(5)));
            }
        }

        [Fact]
        public void Dispose_VaciaLaCola()
        {
            var repo = new Mock<IAuditRepository>();
            repo.Setup(r => r.AddManyAsync(It.IsAny<IEnumerable<AuditEvent>>())).Returns(Task.CompletedTask);
            var sut = new AuditDispatcher(() => repo.Object, SinTimer);
            sut.Enqueue(Evento());

            sut.Dispose();

            repo.Verify(r => r.AddManyAsync(It.IsAny<IEnumerable<AuditEvent>>()), Times.Once);
        }

        [Fact]
        public void ConRepositorioCompartido_NoLoLibera()
        {
            var repo = new RepoDescartable();
            var sut = new AuditDispatcher(repo, SinTimer);
            sut.Enqueue(Evento());

            sut.Dispose();

            Assert.False(repo.Disposed);
        }

        private static AuditEvent Evento() => new AuditEvent
        {
            Id = Guid.NewGuid(),
            Recorded = DateTime.UtcNow,
            Action = "U",
            EntityRef = "QuestionnaireResponse/" + Guid.NewGuid(),
            AgentUserId = "dr.gomez"
        };

        private sealed class RepoDescartable : IAuditRepository, IDisposable
        {
            public bool Disposed { get; private set; }
            public Task AddAsync(AuditEvent evt) => Task.CompletedTask;
            public Task AddManyAsync(IEnumerable<AuditEvent> events) => Task.CompletedTask;
            public Task<IReadOnlyList<AuditEvent>> SearchAsync(string agentUserId, string action, DateTime? fromUtc, DateTime? toUtc, int max)
                => Task.FromResult((IReadOnlyList<AuditEvent>)new List<AuditEvent>());
            public Task<AuditEvent> GetAsync(Guid id) => Task.FromResult<AuditEvent>(null);
            public Task<IReadOnlyList<AuditEvent>> ForEntityAsync(string entityRef, bool includeOperations)
                => Task.FromResult((IReadOnlyList<AuditEvent>)new List<AuditEvent>());
            public void Dispose() => Disposed = true;
        }
    }
}
