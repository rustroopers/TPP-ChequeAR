using System;
using System.Globalization;
using System.Threading;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using Xunit;

namespace ChequeAR.Tests.Application
{
    /// <summary>Un único formato de respuesta para la ficha en lectura y para la auditoría.</summary>
    public class ResponseAnswerFormatterTests
    {
        [Fact]
        public void Decimal_ConComaDecimal_SinImportarLaCulturaDelHilo()
        {
            var previa = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
                Assert.Equal("37,5", ResponseAnswerFormatter.Format(new QuestionnaireResponseItem { ValueDecimal = 37.5m }));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previa;
            }
        }

        [Theory]
        [InlineData(true, "Sí")]
        [InlineData(false, "No")]
        public void Booleano(bool valor, string esperado)
        {
            Assert.Equal(esperado, ResponseAnswerFormatter.Format(new QuestionnaireResponseItem { ValueBoolean = valor }));
        }

        [Fact]
        public void Fecha_EnFormatoIso()
        {
            Assert.Equal("2026-10-06", ResponseAnswerFormatter.Format(new QuestionnaireResponseItem { ValueDate = new DateTime(2026, 10, 6) }));
        }

        [Fact]
        public void OpcionMultiple_SeparaPorComa_YOmiteLosVacios()
        {
            var items = new[]
            {
                new QuestionnaireResponseItem { ValueCode = "Tos" },
                new QuestionnaireResponseItem(),
                new QuestionnaireResponseItem { ValueCode = "Disnea" }
            };

            Assert.Equal("Tos, Disnea", ResponseAnswerFormatter.Format(items));
        }

        [Fact]
        public void SinRespuesta_DevuelveLaMarcaDeVacio()
        {
            Assert.Equal(ResponseAnswerFormatter.Empty, ResponseAnswerFormatter.Format((QuestionnaireResponseItem[])null));
            Assert.Equal(ResponseAnswerFormatter.Empty, ResponseAnswerFormatter.Format(new QuestionnaireResponseItem()));
        }
    }
}
