using System;
using System.Collections.Generic;
using System.Linq;
using ChequeAR.Application.Reports;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using Xunit;

namespace ChequeAR.Tests.Application
{
    public class StatsCalculatorTests
    {
        private static ReportSchema Schema(params (string linkId, QuestionType tipo)[] preguntas)
        {
            var q = new Questionnaire();
            var i = 0;
            foreach (var (linkId, tipo) in preguntas)
                q.Questions.Add(new Question { LinkId = linkId, Text = linkId.ToUpperInvariant(), Type = tipo, OrderIndex = i++ });
            return ReportSchema.From(q);
        }

        private static Dictionary<string, object> Fila(Guid responseId, string linkId, object valor, string columna)
        {
            return new Dictionary<string, object>
            {
                ["ResponseId"] = responseId,
                ["LinkId"] = linkId,
                [columna] = valor
            };
        }

        private static ReportResult Largo(params Dictionary<string, object>[] filas)
            => new ReportResult { Rows = filas.ToList(), Total = filas.Length };

        // n, media, mediana, desvio: los cuatro que pide FR-008.
        [Fact]
        public void Numerica_ResumenBasico()
        {
            var r1 = Guid.NewGuid(); var r2 = Guid.NewGuid(); var r3 = Guid.NewGuid();
            var stats = StatsCalculator.Compute(
                Largo(Fila(r1, "edad", 2m, "ValueInteger"),
                      Fila(r2, "edad", 4m, "ValueInteger"),
                      Fila(r3, "edad", 9m, "ValueInteger")),
                Schema(("edad", QuestionType.Integer)), 3);

            var q = stats.Questions.Single();
            Assert.Equal("numeric", q.Kind);
            Assert.Equal(3, q.N);
            Assert.Equal(5m, q.Mean);       // (2+4+9)/3
            Assert.Equal(4m, q.Median);
            Assert.Equal(2m, q.Min);
            Assert.Equal(9m, q.Max);
            Assert.Equal(3.61m, q.StdDev);  // muestral (n-1)
        }

        // Con una sola observacion el desvio no esta definido. Mostrar 0 se leeria como
        // "no hay dispersion", que es una afirmacion distinta.
        [Fact]
        public void Numerica_UnaSolaObservacion_SinDesvio()
        {
            var stats = StatsCalculator.Compute(
                Largo(Fila(Guid.NewGuid(), "edad", 7m, "ValueInteger")),
                Schema(("edad", QuestionType.Integer)), 1);

            Assert.Null(stats.Questions.Single().StdDev);
        }

        // Interpolacion lineal (tipo 7 de R / numpy / Excel) para que los numeros coincidan
        // si alguien los recalcula por su cuenta.
        [Fact]
        public void Numerica_PercentilesInterpolados()
        {
            var filas = new[] { 1m, 2m, 3m, 4m }
                .Select(v => Fila(Guid.NewGuid(), "eva", v, "ValueDecimal")).ToArray();

            var q = StatsCalculator.Compute(Largo(filas), Schema(("eva", QuestionType.Decimal)), 4).Questions.Single();

            Assert.Equal(2.5m, q.Median);
            Assert.Equal(1.75m, q.P25);
            Assert.Equal(3.25m, q.P75);
        }

        // La completitud se mide sobre la cohorte, no sobre las filas que volvieron: una
        // pregunta que nadie respondio no aparece en el resultado y tiene que dar 0%.
        [Fact]
        public void Completitud_SobreLaCohorte()
        {
            var stats = StatsCalculator.Compute(
                Largo(Fila(Guid.NewGuid(), "edad", 5m, "ValueInteger")),
                Schema(("edad", QuestionType.Integer), ("peso", QuestionType.Decimal)), 4);

            var edad = stats.Questions.Single(x => x.LinkId == "edad");
            Assert.Equal(1, edad.Answered);
            Assert.Equal(3, edad.Missing);
            Assert.Equal(25.0, edad.CompletenessPct);

            var peso = stats.Questions.Single(x => x.LinkId == "peso");
            Assert.Equal(0, peso.Answered);
            Assert.Equal(4, peso.Missing);
            Assert.Equal(0.0, peso.CompletenessPct);
        }

        // Una opcion multiple aporta VARIAS filas con el mismo linkId para la misma ficha.
        // "Respondida" tiene que contarse por ficha distinta, o la completitud pasa el 100%.
        [Fact]
        public void OpcionMultiple_CompletitudPorFichaNoPorFila()
        {
            var r1 = Guid.NewGuid();
            var stats = StatsCalculator.Compute(
                Largo(Fila(r1, "sintomas", "Fiebre", "ValueCode"),
                      Fila(r1, "sintomas", "Rash", "ValueCode"),
                      Fila(r1, "sintomas", "Artritis", "ValueCode")),
                Schema(("sintomas", QuestionType.OpenChoice)), 2);

            var q = stats.Questions.Single();
            Assert.Equal(1, q.Answered);         // una sola ficha
            Assert.Equal(50.0, q.CompletenessPct);
            Assert.Equal(3, q.N);                // tres marcas
            Assert.Equal(3, q.Categories.Count);
        }

        [Fact]
        public void Categorica_ConteosYPorcentajes()
        {
            var stats = StatsCalculator.Compute(
                Largo(Fila(Guid.NewGuid(), "subtipo", "Oligoarticular", "ValueCode"),
                      Fila(Guid.NewGuid(), "subtipo", "Oligoarticular", "ValueCode"),
                      Fila(Guid.NewGuid(), "subtipo", "Sistémica", "ValueCode")),
                Schema(("subtipo", QuestionType.Choice)), 3);

            var q = stats.Questions.Single();
            Assert.Equal("categorical", q.Kind);
            // Ordenadas por frecuencia descendente.
            Assert.Equal("Oligoarticular", q.Categories[0].Label);
            Assert.Equal(2, q.Categories[0].Count);
            Assert.Equal(66.7, q.Categories[0].Pct);
        }

        [Fact]
        public void Booleana_SeEtiquetaSiNo()
        {
            var stats = StatsCalculator.Compute(
                Largo(Fila(Guid.NewGuid(), "rigidez", 1m, "ValueBoolean"),
                      Fila(Guid.NewGuid(), "rigidez", 0m, "ValueBoolean")),
                Schema(("rigidez", QuestionType.Boolean)), 2);

            var labels = stats.Questions.Single().Categories.Select(c => c.Label).OrderBy(x => x);
            Assert.Equal(new[] { "No", "Sí" }, labels);
        }

        // Un archivo no se resume: no tiene media ni categorias.
        [Fact]
        public void Adjunto_SeOmite()
        {
            var stats = StatsCalculator.Compute(
                Largo(), Schema(("radiografia", QuestionType.Attachment)), 3);

            Assert.Empty(stats.Questions);
        }

        [Fact]
        public void CohorteVacia_NoDivideEntreCero()
        {
            var stats = StatsCalculator.Compute(Largo(), Schema(("edad", QuestionType.Integer)), 0);

            var q = stats.Questions.Single();
            Assert.Equal(0, stats.Responses);
            Assert.Equal(0.0, q.CompletenessPct);
            Assert.Equal(0, q.N);
            Assert.Null(q.Mean);
        }

        // Todos los valores iguales: un histograma con ancho de tramo 0 dividiria por cero.
        [Fact]
        public void Histograma_ValoresIdenticos_UnSoloTramo()
        {
            var filas = Enumerable.Range(0, 5)
                .Select(_ => Fila(Guid.NewGuid(), "edad", 7m, "ValueInteger")).ToArray();

            var q = StatsCalculator.Compute(Largo(filas), Schema(("edad", QuestionType.Integer)), 5).Questions.Single();

            Assert.Single(q.Histogram);
            Assert.Equal(5, q.Histogram[0].Count);
        }

        // El ultimo tramo incluye el extremo superior; los demas no. Sin eso, el valor
        // maximo se contaba dos veces o no se contaba en ninguno.
        [Fact]
        public void Histograma_CuentaCadaValorUnaSolaVez()
        {
            var valores = new[] { 1m, 2m, 3m, 4m, 5m, 6m, 7m, 8m };
            var filas = valores.Select(v => Fila(Guid.NewGuid(), "edad", v, "ValueInteger")).ToArray();

            var q = StatsCalculator.Compute(Largo(filas), Schema(("edad", QuestionType.Integer)), 8).Questions.Single();

            Assert.Equal(valores.Length, q.Histogram.Sum(b => b.Count));
        }
    }
}
