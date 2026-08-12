using ChequeAR.Application.Services;
using Xunit;

namespace ChequeAR.Tests.Application
{
    /// <summary>
    /// Nombre legible de especialidad a partir del código. El editor guarda sólo el código
    /// (display en null), así que este mapa es el que evita mostrar "reuma" crudo en la UI.
    /// </summary>
    public class SpecialtyMapTests
    {
        [Fact]
        public void Display_TienePrioridadSobreElCodigo()
        {
            Assert.Equal("Reumatología Pediátrica", SpecialtyMap.Label("reuma", "Reumatología Pediátrica"));
        }

        [Theory]
        [InlineData("general", "General")]
        [InlineData("cardio", "Cardiología")]
        [InlineData("neumo", "Neumonología")]
        [InlineData("onco", "Oncología")]
        [InlineData("reuma", "Reumatología")]
        public void CodigoConocido_DevuelveElLabel(string code, string expected)
        {
            Assert.Equal(expected, SpecialtyMap.Label(code));
        }

        [Fact]
        public void CodigoConocido_EsCaseInsensitiveYToleraEspacios()
        {
            Assert.Equal("Cardiología", SpecialtyMap.Label("  CARDIO  "));
        }

        [Fact]
        public void CodigoDesconocido_SeDevuelveTalCual()
        {
            Assert.Equal("hemato", SpecialtyMap.Label("hemato"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void SinCodigoNiDisplay_CaeAGeneral(string code)
        {
            Assert.Equal("General", SpecialtyMap.Label(code));
        }
    }
}
