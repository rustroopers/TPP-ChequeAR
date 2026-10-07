using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Dtos;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace ChequeAR.Tests.Application
{
    /// <summary>
    /// Los accesos de lectura (ver un reporte, exportarlo, mirar la evolución de un paciente,
    /// bajar la auditoría) tienen que quedar registrados con QUÉ se consultó (FR-009).
    /// </summary>
    public class AccessAuditServiceTests
    {
        private readonly List<AuditEvent> _eventos = new List<AuditEvent>();
        private readonly AuditDispatcher _dispatcher;
        private readonly AccessAuditService _sut;
        private readonly Guid _cuestionario = Guid.NewGuid();

        public AccessAuditServiceTests()
        {
            var repo = new Mock<IAuditRepository>();
            repo.Setup(r => r.AddManyAsync(It.IsAny<IEnumerable<AuditEvent>>()))
                .Callback<IEnumerable<AuditEvent>>(lote => _eventos.AddRange(lote))
                .Returns(Task.CompletedTask);
            _dispatcher = new AuditDispatcher(repo.Object, flushIntervalMs: 600000);

            var usuario = new Mock<ICurrentUser>();
            usuario.SetupGet(u => u.Id).Returns("dr.gomez");
            _sut = new AccessAuditService(_dispatcher, usuario.Object);
        }

        private async Task<AuditEvent> UnicoEventoAsync()
        {
            await _dispatcher.FlushAsync();
            return Assert.Single(_eventos);
        }

        private ReportQuery Consulta(int page = 1) => new ReportQuery
        {
            QuestionnaireId = _cuestionario,
            Page = page,
            Authors = new List<string> { "dra.perez" },
            DateRange = new DateRange { From = new DateTime(2026, 1, 1), To = new DateTime(2026, 3, 31) }
        };

        [Fact]
        public async Task ReportViewed_RegistraUnaLecturaConLaConsultaCompleta()
        {
            _sut.ReportViewed(Consulta(), total: 42);

            var evt = await UnicoEventoAsync();
            Assert.Equal("R", evt.Action);
            Assert.Equal("dr.gomez", evt.AgentUserId);
            Assert.Equal("Report", evt.EntityType);
            Assert.Equal("Report/query?questionnaire=" + _cuestionario + "&rows=42", evt.EntityRef);

            var foto = JsonConvert.DeserializeObject<SavedReportAuditSnapshot>(evt.FhirJson);
            Assert.Equal(_cuestionario, foto.QuestionnaireId);
            var consulta = JsonConvert.DeserializeObject<ReportQuery>(foto.QueryJson);
            Assert.Equal("dra.perez", Assert.Single(consulta.Authors));
            Assert.Equal(new DateTime(2026, 1, 1), consulta.DateRange.From);
        }

        // Pasar de página es seguir mirando la misma consulta: no se registra de nuevo.
        [Fact]
        public async Task ReportViewed_NoRegistraLasPaginasSiguientes()
        {
            _sut.ReportViewed(Consulta(page: 2), total: 42);

            await _dispatcher.FlushAsync();
            Assert.Empty(_eventos);
        }

        [Fact]
        public async Task StatsViewed_RegistraUnaLectura()
        {
            _sut.StatsViewed(Consulta(), questions: 7);

            var evt = await UnicoEventoAsync();
            Assert.Equal("R", evt.Action);
            Assert.Equal("Report/stats?questionnaire=" + _cuestionario + "&questions=7", evt.EntityRef);
        }

        [Fact]
        public async Task ReportExported_RegistraLaExportacionConSusFiltros()
        {
            _sut.ReportExported(Consulta(), total: 300);

            var evt = await UnicoEventoAsync();
            Assert.Equal("E", evt.Action);
            Assert.Equal("export", evt.Category);
            Assert.Equal("Report/export?questionnaire=" + _cuestionario + "&rows=300", evt.EntityRef);
            var foto = JsonConvert.DeserializeObject<SavedReportAuditSnapshot>(evt.FhirJson);
            Assert.Contains("dra.perez", foto.QueryJson);
        }

        [Fact]
        public async Task StatsExported_RegistraLaExportacionConSusFiltros()
        {
            _sut.StatsExported(Consulta(), questions: 7);

            var evt = await UnicoEventoAsync();
            Assert.Equal("E", evt.Action);
            Assert.Equal("Report/statsExport?questionnaire=" + _cuestionario + "&questions=7", evt.EntityRef);
            Assert.NotNull(evt.FhirJson);
        }

        [Fact]
        public async Task TrendViewed_RegistraPacienteCuestionarioYParametros()
        {
            _sut.TrendViewed("HC-123", _cuestionario, new[] { "peso", "talla" });

            var evt = await UnicoEventoAsync();
            Assert.Equal("R", evt.Action);
            Assert.Equal("Patient", evt.EntityType);
            Assert.Equal("Patient/HC-123$trend", evt.EntityRef);

            var foto = JsonConvert.DeserializeObject<TrendAuditSnapshot>(evt.FhirJson);
            Assert.Equal("HC-123", foto.PatientHospitalId);
            Assert.Equal(_cuestionario, foto.QuestionnaireId);
            Assert.Equal(new[] { "peso", "talla" }, foto.LinkIds);
        }

        [Fact]
        public async Task AuditExported_RegistraCuantosEventosYConQueFiltros()
        {
            _sut.AuditExported(new AuditExportSnapshot { User = "admin.demo", From = "2026-01-01", Format = "csv" }, rows: 187);

            var evt = await UnicoEventoAsync();
            Assert.Equal("E", evt.Action);
            Assert.Equal("AuditEvent", evt.EntityType);
            Assert.Equal("AuditEvent/export?rows=187", evt.EntityRef);

            var foto = JsonConvert.DeserializeObject<AuditExportSnapshot>(evt.FhirJson);
            Assert.Equal("admin.demo", foto.User);
            Assert.Equal("2026-01-01", foto.From);
        }

        [Fact]
        public async Task ConsultaNula_NoRegistraNada()
        {
            _sut.ReportViewed(null, 0);
            _sut.StatsViewed(null, 0);
            _sut.ReportExported(null, 0);
            _sut.StatsExported(null, 0);

            await _dispatcher.FlushAsync();
            Assert.Empty(_eventos);
        }
    }
}
