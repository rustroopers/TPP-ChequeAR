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
    /// <summary>Filtrado, paginación y búsquedas por recurso del registro de auditoría.</summary>
    public class AuditQueryServiceTests
    {
        private readonly Mock<IAuditRepository> _repo = new Mock<IAuditRepository>();
        private readonly AuditQueryService _sut;

        public AuditQueryServiceTests() { _sut = new AuditQueryService(_repo.Object); }

        // La búsqueda filtra, cuenta y pagina en la base: el servicio arma los criterios,
        // acota la página y devuelve lo que la base respondió.
        [Fact]
        public async Task Buscar_PasaTodosLosFiltrosALaBase()
        {
            var t0 = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
            AuditSearchCriteria recibido = null;
            _repo.Setup(r => r.SearchAsync(It.IsAny<AuditSearchCriteria>(), It.IsAny<int>(), It.IsAny<int>()))
                 .Callback<AuditSearchCriteria, int, int>((c, s, t) => recibido = c)
                 .ReturnsAsync(new AuditPage());

            await _sut.SearchAsync(agentUserId: "gomez", action: "U", entityRef: "Questionnaire/a",
                fromUtc: t0, toUtc: t0.AddDays(1));

            Assert.Equal("gomez", recibido.AgentUserId);
            Assert.Equal("U", recibido.Action);
            Assert.Equal("Questionnaire/a", recibido.EntityRef);
            Assert.Equal(t0, recibido.FromUtc);
            Assert.Equal(t0.AddDays(1), recibido.ToUtc);
        }

        /// <summary>
        /// El total es el de la búsqueda completa, no el de la página. Con el de la página, la
        /// última parece la única y el paginador desaparece.
        /// </summary>
        [Fact]
        public async Task ElTotalEsElQueInformaLaBase_NoElDeLaPagina()
        {
            _repo.Setup(r => r.SearchAsync(It.IsAny<AuditSearchCriteria>(), 20, 10))
                 .ReturnsAsync(new AuditPage { Total = 12000, Items = new List<AuditEvent> { Evento("a") } });

            var pagina = await _sut.SearchAsync(skip: 20, take: 10);

            Assert.Equal(12000, pagina.Total);
            Assert.Single(pagina.Items);
        }

        [Theory]
        [InlineData(-5, 0, 0, 1)]
        [InlineData(0, 1000000, 0, AuditQueryService.MaxPageSize)]
        public async Task Buscar_AcotaDesplazamientoYTamanoDePagina(int skip, int take, int skipEsperado, int takeEsperado)
        {
            _repo.Setup(r => r.SearchAsync(It.IsAny<AuditSearchCriteria>(), It.IsAny<int>(), It.IsAny<int>()))
                 .ReturnsAsync(new AuditPage());

            await _sut.SearchAsync(skip: skip, take: take);

            _repo.Verify(r => r.SearchAsync(It.IsAny<AuditSearchCriteria>(), skipEsperado, takeEsperado), Times.Once);
        }

        [Fact]
        public async Task Exportar_DevuelveTodosLosEventosDeLaBusqueda()
        {
            var eventos = new List<AuditEvent> { Evento("a"), Evento("b") };
            _repo.Setup(r => r.SearchAsync(It.IsAny<AuditSearchCriteria>(), 0, AuditQueryService.MaxExportRows))
                 .ReturnsAsync(new AuditPage { Total = 2, Items = eventos });

            Assert.Equal(eventos, await _sut.ExportAsync(new AuditSearchCriteria()));
        }

        // Un export de auditoría recortado que parece completo es peor que un error.
        [Fact]
        public async Task Exportar_SiSuperaElTope_SeRechazaEnVezDeRecortar()
        {
            _repo.Setup(r => r.SearchAsync(It.IsAny<AuditSearchCriteria>(), It.IsAny<int>(), It.IsAny<int>()))
                 .ReturnsAsync(new AuditPage { Total = AuditQueryService.MaxExportRows + 1 });

            await Assert.ThrowsAsync<ChequeAR.Application.Exceptions.BusinessRuleException>(
                () => _sut.ExportAsync(new AuditSearchCriteria()));
        }

        [Fact]
        public async Task BuscarPorIdentificador_DevuelveNullSiNoExiste()
        {
            _repo.Setup(r => r.GetAsync(It.IsAny<Guid>())).ReturnsAsync((AuditEvent)null);

            Assert.Null(await _sut.GetAsync(Guid.NewGuid()));
        }

        /// <summary>
        /// Va directo por id a la base. Antes se buscaba dentro de los últimos 5000 eventos,
        /// así que el detalle de un evento viejo daba 404.
        /// </summary>
        [Fact]
        public async Task BuscarPorIdentificador_VaPorIdALaBase_SinVentanaDeEventos()
        {
            var e = Evento("a");
            _repo.Setup(r => r.GetAsync(e.Id)).ReturnsAsync(e);

            Assert.Equal(e.Id, (await _sut.GetAsync(e.Id)).Id);
            _repo.Verify(r => r.SearchAsync(It.IsAny<AuditSearchCriteria>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task ForResponse_PideALaBaseLosEventosDeLaFichaConSusOperaciones()
        {
            var id = Guid.NewGuid();
            Devuelve();

            await _sut.ForResponseAsync(id);

            _repo.Verify(r => r.ForEntityAsync("QuestionnaireResponse/" + id, true), Times.Once);
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

        /// <summary>
        /// El doble devuelve el mismo conjunto para cualquier recurso: el servicio es el
        /// responsable de quedarse sólo con lo que corresponde.
        /// </summary>
        private void Devuelve(params AuditEvent[] eventos)
        {
            _repo.Setup(r => r.ForEntityAsync(It.IsAny<string>(), It.IsAny<bool>()))
                 .ReturnsAsync((IReadOnlyList<AuditEvent>)eventos.ToList());
        }

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
