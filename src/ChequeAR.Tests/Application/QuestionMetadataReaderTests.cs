using ChequeAR.Application.Services;
using Xunit;

namespace ChequeAR.Tests.Application
{
    /// <summary>
    /// Los contratos JSON de la pregunta se leen de forma tolerante: una fila mal guardada no
    /// tumba ni la pantalla ni la publicación.
    /// </summary>
    public class QuestionMetadataReaderTests
    {
        [Fact]
        public void ReadValidation_ValoresConTipoEquivocado_SeLeenComoAusentes()
        {
            var v = QuestionMetadataReader.ReadValidation("{\"min\":\"abc\",\"max\":[1],\"maxLength\":2.5,\"unit\":\"kg\"}");

            Assert.Null(v.Min);
            Assert.Null(v.Max);
            Assert.Null(v.MaxLength);
            Assert.Equal("kg", v.Unit);
        }

        [Fact]
        public void ReadValidation_NumeroComoTexto_AceptaComaDecimal()
        {
            Assert.Equal(12.5m, QuestionMetadataReader.ReadValidation("{\"min\":\"12,5\"}").Min);
        }

        [Fact]
        public void ReadValidation_JsonCorrupto_DevuelveNull()
        {
            Assert.Null(QuestionMetadataReader.ReadValidation("{no es json"));
        }

        // Una condición recién creada en el editor tiene value = "": está incompleta.
        [Fact]
        public void ReadCondition_ValorVacio_EsCondicionIncompleta()
        {
            var c = QuestionMetadataReader.ReadCondition("{\"question\":\"fuma\",\"op\":\"=\",\"value\":\"\"}");

            Assert.Equal("fuma", c.Question);
            Assert.Null(c.Value);
        }

        // El término de autoría se distingue del display que se usa para mostrar.
        [Fact]
        public void ReadCoding_SinTerminoExplicito_NoInventaTerminoDeAutoria()
        {
            var c = QuestionMetadataReader.ReadCoding("{\"code\":\"29857009\",\"display\":\"dolor\"}");

            Assert.Equal("dolor", c.Term);
            Assert.Null(c.AuthoredTerm);
        }
    }
}
