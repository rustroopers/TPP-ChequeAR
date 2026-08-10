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
    /// Filtrado y paginación de auditoría. Vivía repartido en dos controllers, que traían
    /// 5000 filas y filtraban en memoria — sin ninguna prueba, porque no se puede probar sin
    /// levantar la capa web.
    /// </summary>
    public class AuditQueryServiceTests
    {
        private readonly Mock<IAuditRepository> _repo = new Mock<IAuditRepository>();
        private readonly AuditQueryService _sut;

        public AuditQueryServiceTests() { _sut = new AuditQueryService(_repo.Object); }

        [Fact]
        public async Task FiltraPorRecursoAfectado()
        {
            Devuelve(Evento("Questionnaire/a"), Evento("Questionnaire/b"), Evento("Questionnaire/a"));

            var pagina = await _sut.SearchAsync(entityRef: "Questionnaire/a");

            Assert.Equal(2, pagina.Total);
            Assert.All(pagina.Items, e => Assert.Equal("Questionnaire/a", e.EntityRef));
        }

        /// <summary>
        /// El total es el de la búsqueda completa, no el de la página. Con el de la página, la
        /// última parece la única y el paginador desaparece.
        /// </summary>
        [Fact]
        public async Task ElTotalEsDeLaBusquedaNoDeLaPagina()
        {
            Devuelve(Enumerable.Range(0, 25).Select(i => Evento("Questionnaire/" + i)).ToArray());

            var pagina = await _sut.SearchAsync(skip: 0, take: 10);

            Assert.Equal(25, pagina.Total);
            Assert.Equal(10, pagina.Items.Count);
        }

        [Fact]
        public async Task PaginaDesdeElDesplazamientoPedido()
        {
            Devuelve(Evento("r/0"), Evento("r/1"), Evento("r/2"), Evento("r/3"));

            var pagina = await _sut.SearchAsync(skip: 2, take: 2);

            Assert.Equal(new[] { "r/2", "r/3" }, pagina.Items.Select(e => e.EntityRef));
        }

        [Fact]
        public async Task SinFiltroDeRecurso_DevuelveTodo()
        {
            Devuelve(Evento("a"), Evento("b"));

            Assert.Equal(2, (await _sut.SearchAsync()).Total);
        }

        [Fact]
        public async Task BuscarPorIdentificador_DevuelveNullSiNoExiste()
        {
            Devuelve(Evento("a"));

            Assert.Null(await _sut.GetAsync(Guid.NewGuid()));
        }

        [Fact]
        public async Task BuscarPorIdentificador_EncuentraElEvento()
        {
            var e = Evento("a");
            Devuelve(e);

            Assert.Equal(e.Id, (await _sut.GetAsync(e.Id)).Id);
        }

        private void Devuelve(params AuditEvent[] eventos)
            => _repo.Setup(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<string>(),
                                              It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>()))
                    .ReturnsAsync((IReadOnlyList<AuditEvent>)eventos.ToList());

        private static AuditEvent Evento(string entityRef) => new AuditEvent
        {
            Id = Guid.NewGuid(),
            EntityRef = entityRef,
            Action = "U",
            AgentUserId = "dr.gomez",
            Recorded = DateTime.UtcNow
        };
    }
}
