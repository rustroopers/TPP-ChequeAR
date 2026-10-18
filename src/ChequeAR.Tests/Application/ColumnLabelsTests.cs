using System;
using System.Collections.Generic;
using ChequeAR.Application.Reports;
using ChequeAR.Domain.Entities;
using Xunit;

namespace ChequeAR.Tests.Application
{
    public class ColumnLabelsTests
    {
        private static readonly Guid Vieja = Guid.NewGuid();
        private static readonly Guid Nueva = Guid.NewGuid();

        private static readonly Dictionary<Guid, string> Nombres = new Dictionary<Guid, string>
        {
            [Vieja] = "Peso",
            [Nueva] = "Peso pediátrico"
        };

        // El Jefe suele dejar el mismo enunciado al reemplazar la variable: sin esto el reporte
        // mostraría dos columnas "Peso" sin forma de saber cuál es cuál.
        [Fact]
        public void Reemplazada_SeMarcaConLaQueLaReemplaza()
        {
            var vieja = new Question { LinkId = "peso-1", Text = "Peso", VariableId = Vieja };
            var nueva = new Question { LinkId = "peso-2-1", Text = "Peso", VariableId = Nueva };

            ColumnLabels.Apply(new[] { vieja, nueva }, Nombres, new Dictionary<Guid, string> { [Vieja] = "Peso pediátrico" });

            Assert.Equal("Peso (reemplazada por Peso pediátrico)", vieja.Text);
            Assert.Equal("Peso", nueva.Text);
        }

        [Fact]
        public void TextosRepetidos_SeDistinguenPorElNombreDeLaVariable()
        {
            var a = new Question { LinkId = "peso-1", Text = "Peso", VariableId = Vieja };
            var b = new Question { LinkId = "peso-2-1", Text = "peso", VariableId = Nueva };

            ColumnLabels.Apply(new[] { a, b }, Nombres, new Dictionary<Guid, string>());

            Assert.Equal("Peso (Peso)", a.Text);
            Assert.Equal("peso (Peso pediátrico)", b.Text);
        }

        [Fact]
        public void SinReemplazosNiRepetidos_NoCambiaNada()
        {
            var a = new Question { LinkId = "peso-1", Text = "Peso al nacer", VariableId = Vieja };
            var b = new Question { LinkId = "talla-1", Text = "Talla" };

            ColumnLabels.Apply(new[] { a, b }, Nombres, new Dictionary<Guid, string>());

            Assert.Equal("Peso al nacer", a.Text);
            Assert.Equal("Talla", b.Text);
        }
    }
}
