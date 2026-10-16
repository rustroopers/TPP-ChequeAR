using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http.Results;
using System.Web.Mvc;
using ChequeAR.Api.Controllers.Api;
using ChequeAR.Api.Controllers.Mvc;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Entities;
using Moq;
using Xunit;

namespace ChequeAR.Api.Tests
{
    public class HealthAndErrorControllerTests
    {
        [Fact]
        public async Task Health_ConBase_Ok()
        {
            var repo = new Mock<ISpecialtyRepository>();
            repo.Setup(r => r.ListAsync(false)).ReturnsAsync((IReadOnlyList<Specialty>)new List<Specialty>());

            var result = await new HealthController(repo.Object, new Mock<ISchemaMigrator>().Object).Get();

            Assert.Equal(typeof(OkNegotiatedContentResult<>), result.GetType().GetGenericTypeDefinition());
        }

        // El smoke del deploy reintenta mientras responda 503: tiene que ser 503 y no un 500.
        [Fact]
        public async Task Health_SinBase_503()
        {
            var repo = new Mock<ISpecialtyRepository>();
            repo.Setup(r => r.ListAsync(false)).ThrowsAsync(new InvalidOperationException("ORA-12541"));

            var result = await new HealthController(repo.Object, new Mock<ISchemaMigrator>().Object).Get();

            var status = (HttpStatusCode)result.GetType().GetProperty("StatusCode").GetValue(result);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        }

        [Theory]
        [InlineData(403, 403)]
        [InlineData(404, 404)]
        [InlineData(500, 500)]
        [InlineData(5000, 500)]
        [InlineData(200, 500)]
        public void Error_SoloCodigosConocidos(int pedido, int esperado)
        {
            var response = new Mock<HttpResponseBase>();
            response.SetupProperty(r => r.StatusCode);
            response.SetupProperty(r => r.TrySkipIisCustomErrors);
            var http = new Mock<HttpContextBase>();
            http.Setup(h => h.Response).Returns(response.Object);
            var controller = new ErrorController();
            controller.ControllerContext = new ControllerContext(http.Object, new System.Web.Routing.RouteData(), controller);

            controller.Index(pedido);

            Assert.Equal(esperado, response.Object.StatusCode);
            Assert.True(response.Object.TrySkipIisCustomErrors);
        }
    }
}
