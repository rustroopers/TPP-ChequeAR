using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Results;
using ChequeAR.Api.Controllers.Api;
using ChequeAR.Application.Interfaces;
using Moq;
using Xunit;

namespace ChequeAR.Api.Tests
{
    public class DeployControllerTests
    {
        // Sin Deploy:Key configurada el endpoint no existe, ni siquiera para consultar el estado.
        [Fact]
        public void SinClaveConfigurada_NoExiste()
        {
            var migrator = new Mock<ISchemaMigrator>();
            var controller = new DeployController(migrator.Object) { Request = new HttpRequestMessage(), Configuration = new HttpConfiguration() };
            controller.Request.Headers.Add("X-Deploy-Key", "cualquiera");

            Assert.IsType<NotFoundResult>(controller.Start());
            Assert.IsType<NotFoundResult>(controller.Status());
            migrator.Verify(m => m.Pending(), Times.Never);
        }
    }
}
