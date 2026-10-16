using System.Web.Http;
using ChequeAR.Api.App_Start;
using Xunit;

namespace ChequeAR.Api.Tests
{
    public class ServiceCompositionTests
    {
        // Configure termina en container.Verify(): un registro que SimpleInjector no puede
        // resolver tumba la app al arrancar, y sin este test recién se veía al desplegar.
        [Fact]
        public void ElContenedorSeArmaYVerifica()
        {
            var container = ServiceComposition.Configure(new HttpConfiguration());

            Assert.NotNull(container);
        }
    }
}
