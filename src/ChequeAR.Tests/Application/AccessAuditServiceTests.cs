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
    /// Los accesos de lectura (exportar un reporte, mirar la evolución de un paciente, bajar
    /// la auditoría) tienen que quedar registrados con QUÉ se consultó (FR-009).
    /// </summary>
    public sealed class AccessAuditServiceTests : IDisposable
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
            repo.Setup(r => r.AddAsync(It.IsAny<AuditEvent>()))
                .Callback<AuditEvent>(e => _eventos.Add(e))
                .Returns(Task.CompletedTask);
            _dispatcher = new AuditDispatcher(repo.Object, flushIntervalMs: 600000);

            var usuario = new Mock<ICurrentUser>();
            usuario.SetupGet(u => u.Id).Returns("dr.gomez");
            _sut = new AccessAuditService(_dispatcher, usuario.Object);
        }

        public void Dispose() => _dispatcher.Dispose();

        // --- Lectura de fichas ------------------------------------------------------------

        [Fact]
        public async Task ResponseRead_FichaFinalizada_SeRegistraEnElActo()
        {
            var id = Guid.NewGuid();
            await _sut.ResponseRead(new QuestionnaireResponse { Id = id, Status = ChequeAR.Domain.Enums.ResponseStatus.Completed });

            // Sin FlushAsync: se persiste en el acto, no espera a la cola.
            var evt = Assert.Single(_eventos);
            Assert.Equal("R", evt.Action);
            Assert.Equal("dr.gomez", evt.AgentUserId);
            Assert.Equal("QuestionnaireResponse/" + id, evt.EntityRef);
        }

        [Fact]
        public async Task ResponseRead_BorradorQueEditaLaMismaPersona_NoSeRegistra()
        {
            await _sut.ResponseRead(new QuestionnaireResponse
            {
                Id = Guid.NewGuid(), Status = ChequeAR.Domain.Enums.ResponseStatus.InProgress, LockedByUserId = "dr.gomez"
            });

            await _dispatcher.FlushAsync();
            Assert.Empty(_eventos);
        }

        [Fact]
        public async Task ResponseRead_BorradorQueEditaOtraPersona_SeRegistra()
        {
            await _sut.ResponseRead(new QuestionnaireResponse
            {
                Id = Guid.NewGuid(), Status = ChequeAR.Domain.Enums.ResponseStatus.InProgress, LockedByUserId = "dra.perez"
            });

            Assert.Single(_eventos);
        }

        [Fact]
        public async Task ResponseRead_BorradorPropioSinLock_NoSeRegistra()
        {
            await _sut.ResponseRead(new QuestionnaireResponse
            {
                Id = Guid.NewGuid(), Status = ChequeAR.Domain.Enums.ResponseStatus.InProgress, AuthorUserId = "dr.gomez"
            });

            await _dispatcher.FlushAsync();
            Assert.Empty(_eventos);
        }

        [Fact]
        public async Task ResponseRead_FichaFinalizadaPropia_SeRegistra()
        {
            await _sut.ResponseRead(new QuestionnaireResponse
            {
                Id = Guid.NewGuid(), Status = ChequeAR.Domain.Enums.ResponseStatus.Completed, AuthorUserId = "dr.gomez"
            });

            Assert.Single(_eventos);
        }

        private async Task<AuditEvent> UnicoEventoAsync()
        {
            await _dispatcher.FlushAsync();
            return Assert.Single(_eventos);
        }

        private ReportQuery Consulta() => new ReportQuery
        {
            QuestionnaireId = _cuestionario,
            Authors = new List<string> { "dra.perez" },
            DateRange = new DateRange { From = new DateTime(2026, 1, 1), To = new DateTime(2026, 3, 31) }
        };

        [Fact]
        public async Task ReportExported_RegistraLaExportacionConLaConsultaCompleta()
        {
            _sut.ReportExported(Consulta(), total: 300);

            var evt = await UnicoEventoAsync();
            Assert.Equal("E", evt.Action);
            Assert.Equal("export", evt.Category);
            Assert.Equal("dr.gomez", evt.AgentUserId);
            Assert.Equal("Report", evt.EntityType);
            Assert.Equal("Report/export?questionnaire=" + _cuestionario + "&rows=300", evt.EntityRef);

            var foto = JsonConvert.DeserializeObject<SavedReportAuditSnapshot>(evt.FhirJson);
            Assert.Equal(_cuestionario, foto.QuestionnaireId);
            var consulta = JsonConvert.DeserializeObject<ReportQuery>(foto.QueryJson);
            Assert.Equal("dra.perez", Assert.Single(consulta.Authors));
            Assert.Equal(new DateTime(2026, 1, 1), consulta.DateRange.From);
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
            _sut.ReportExported(null, 0);
            _sut.StatsExported(null, 0);

            await _dispatcher.FlushAsync();
            Assert.Empty(_eventos);
        }

        // El nombre o el DNI buscado no queda en la base: sólo su hash.
        [Fact]
        public async Task PatientsSearched_GuardaElHashNoElTermino()
        {
            await _sut.PatientsSearched(" Pérez ", 3);

            var evt = Assert.Single(_eventos);
            Assert.Equal("Patient$search", evt.EntityRef);
            Assert.DoesNotContain("rez", evt.FhirJson, StringComparison.OrdinalIgnoreCase);
            var foto = JsonConvert.DeserializeObject<PatientSearchAuditSnapshot>(evt.FhirJson);
            Assert.Equal(3, foto.Results);
            Assert.Equal(64, foto.TermSha256.Length);
        }

        [Fact]
        public async Task PacientesYAdjuntos_SeRegistranEnElActo()
        {
            var adjunto = Guid.NewGuid();
            await _sut.PatientRead("PAC-1");
            await _sut.PatientHistoryViewed("PAC-1");
            await _sut.AttachmentUploaded(adjunto);
            await _sut.AttachmentDownloaded(adjunto);

            Assert.Equal(new[] { "Patient/PAC-1", "Patient/PAC-1$history", "Attachment/" + adjunto, "Attachment/" + adjunto },
                _eventos.Select(e => e.EntityRef));
            Assert.Equal(new[] { "R", "R", "C", "R" }, _eventos.Select(e => e.Action));
        }

        [Fact]
        public async Task Dispatcher_SellaLaIpDeQuienPide()
        {
            _dispatcher.AgentIpProvider = () => "10.1.2.3";

            await _sut.PatientRead("PAC-1");
            _sut.AuditExported(new AuditExportSnapshot(), 1);
            await _dispatcher.FlushAsync();

            Assert.All(_eventos, e => Assert.Equal("10.1.2.3", e.AgentIp));
        }
    }
}
