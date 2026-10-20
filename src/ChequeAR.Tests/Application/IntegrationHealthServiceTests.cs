using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Services;
using Moq;
using Xunit;

namespace ChequeAR.Tests.Application
{
    public class IntegrationHealthServiceTests
    {
        private readonly Mock<IOracleViewClient> _oracle = new Mock<IOracleViewClient>();
        private readonly Mock<IHospitalFhirClient> _fhir = new Mock<IHospitalFhirClient>();
        private readonly List<string> _log = new List<string>();

        private IntegrationHealthService Sut()
        {
            var log = new Mock<IOperationalLog>();
            log.Setup(l => l.Warning(It.IsAny<Exception>(), It.IsAny<string>()))
               .Callback<Exception, string>((ex, m) => _log.Add(m + " | " + ex?.Message));
            return new IntegrationHealthService(_fhir.Object, _oracle.Object, new Mock<IFhirValidator>().Object, log.Object);
        }

        // Antes consultaba DUAL como si fuera una vista y la allowlist lo rechazaba siempre.
        [Fact]
        public async Task Oracle_ConPing_Ok()
        {
            _oracle.Setup(o => o.PingAsync()).Returns(Task.CompletedTask);

            var oracle = (await Sut().ProbeAllAsync()).Single(s => s.Name == "Oracle Vistas");

            Assert.Equal("ok", oracle.Status);
        }

        [Fact]
        public async Task Oracle_SinConexion_DownConTextoGenerico_YElDetalleAlLog()
        {
            _oracle.Setup(o => o.PingAsync()).ThrowsAsync(new InvalidOperationException("ORA-12541: TNS:no listener en db-hosp:1521"));

            var oracle = (await Sut().ProbeAllAsync()).Single(s => s.Name == "Oracle Vistas");

            Assert.Equal("down", oracle.Status);
            Assert.DoesNotContain("ORA", oracle.Detail);
            Assert.Contains(_log, m => m.Contains("ORA-12541"));
        }

        [Fact]
        public async Task LosProbesCorrenEnParalelo()
        {
            var enCurso = 0;
            var vieronAlOtro = 0;
            var todos = new TaskCompletionSource<bool>();
            Func<Task> barrera = async () =>
            {
                if (Interlocked.Increment(ref enCurso) == 2) todos.TrySetResult(true);
                if (await Task.WhenAny(todos.Task, Task.Delay(30000)) == todos.Task)
                    Interlocked.Increment(ref vieronAlOtro);
            };
            _fhir.Setup(f => f.GetPatientAsync(It.IsAny<string>())).Returns(async () => { await barrera(); return (string)null; });
            _oracle.Setup(o => o.PingAsync()).Returns(() => barrera());

            var estados = await Sut().ProbeAllAsync();

            Assert.Equal(2, vieronAlOtro);
            Assert.All(estados.Where(s => s.Name != "Validador FHIR"), s => Assert.Equal("ok", s.Status));
        }

        [Fact]
        public async Task UnProbeQueFalla_NoTumbaAlOtro()
        {
            _fhir.Setup(f => f.GetPatientAsync(It.IsAny<string>())).ThrowsAsync(new TimeoutException("fhir"));
            _oracle.Setup(o => o.PingAsync()).Returns(Task.CompletedTask);

            var estados = await Sut().ProbeAllAsync();

            Assert.Equal("down", estados.Single(s => s.Name == "FHIR Hospital").Status);
            Assert.Equal("ok", estados.Single(s => s.Name == "Oracle Vistas").Status);
        }
    }
}
