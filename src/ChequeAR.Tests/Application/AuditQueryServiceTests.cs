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

        // ---- ForResponseAsync: historial de UNA ficha (pantalla "Detalle") ----

        [Fact]
        public async Task ForResponse_TraeCreacionYOperacionesDeEsaFicha()
        {
            var id = Guid.NewGuid();
            var otra = Guid.NewGuid();
            Devuelve(
                Evento("QuestionnaireResponse/" + id),
                Evento("QuestionnaireResponse/" + id + "$save"),
                Evento("QuestionnaireResponse/" + id + "$finalize"),
                Evento("QuestionnaireResponse/" + otra + "$save"),   // otra ficha
                Evento("Questionnaire/" + id));                      // otro recurso, mismo guid

            var eventos = await _sut.ForResponseAsync(id);

            Assert.Equal(3, eventos.Count);
            Assert.All(eventos, e => Assert.StartsWith("QuestionnaireResponse/" + id, e.EntityRef));
        }

        [Fact]
        public async Task ForResponse_OrdenaDelMasRecienteAlMasViejo()
        {
            var id = Guid.NewGuid();
            var t0 = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
            Devuelve(
                EventoEn("QuestionnaireResponse/" + id + "$save", t0),
                EventoEn("QuestionnaireResponse/" + id + "$finalize", t0.AddMinutes(5)),
                EventoEn("QuestionnaireResponse/" + id, t0.AddMinutes(-5)));

            var refs = (await _sut.ForResponseAsync(id)).Select(e => e.EntityRef).ToArray();

            Assert.Equal(new[]
            {
                "QuestionnaireResponse/" + id + "$finalize",
                "QuestionnaireResponse/" + id + "$save",
                "QuestionnaireResponse/" + id
            }, refs);
        }

        [Fact]
        public async Task ForResponse_NoConfundeFichasDistintas()
        {
            var id = Guid.NewGuid();
            Devuelve(Evento("QuestionnaireResponse/" + Guid.NewGuid() + "$finalize"));

            Assert.Empty(await _sut.ForResponseAsync(id));
        }

        [Fact]
        public async Task ForResponse_ExigeSeparadorExacto_NoMatcheaPorPrefijoTextual()
        {
            var id = Guid.NewGuid();
            // Mismo id como prefijo pero sin el separador '$': no es un evento de esta ficha.
            Devuelve(Evento("QuestionnaireResponse/" + id + "-otro"));

            Assert.Empty(await _sut.ForResponseAsync(id));
        }

        private void Devuelve(params AuditEvent[] eventos)
            => _repo.Setup(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<string>(),
                                              It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>()))
                    .ReturnsAsync((IReadOnlyList<AuditEvent>)eventos.ToList());

        private static AuditEvent Evento(string entityRef) => EventoEn(entityRef, DateTime.UtcNow);

        private static AuditEvent EventoEn(string entityRef, DateTime recorded) => new AuditEvent
        {
            Id = Guid.NewGuid(),
            EntityRef = entityRef,
            Action = "U",
            AgentUserId = "dr.gomez",
            Recorded = recorded
        };
    }
}
