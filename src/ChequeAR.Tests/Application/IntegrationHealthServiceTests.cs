using System;
using System.Linq;
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

        private IntegrationHealthService Sut()
            => new IntegrationHealthService(new Mock<IHospitalFhirClient>().Object, _oracle.Object, null,
                new Mock<IHttpProbe>().Object, new Mock<IFhirValidator>().Object);

        // Antes consultaba DUAL como si fuera una vista y la allowlist lo rechazaba siempre.
        [Fact]
        public async Task Oracle_ConPing_Ok()
        {
            _oracle.Setup(o => o.PingAsync()).Returns(Task.CompletedTask);

            var oracle = (await Sut().ProbeAllAsync()).Single(s => s.Name == "Oracle Vistas");

            Assert.Equal("ok", oracle.Status);
        }

        [Fact]
        public async Task Oracle_SinConexion_Down()
        {
            _oracle.Setup(o => o.PingAsync()).ThrowsAsync(new InvalidOperationException("sin red"));

            var oracle = (await Sut().ProbeAllAsync()).Single(s => s.Name == "Oracle Vistas");

            Assert.Equal("down", oracle.Status);
        }
    }
}
