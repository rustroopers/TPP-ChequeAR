using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Application.Reports
{
    /// <summary>
    /// Estadísticas descriptivas sobre la cohorte filtrada (FR-008), calculadas a partir
    /// del resultado en formato largo: una fila por respuesta-pregunta, con el valor ya en
    /// su columna tipada.
    ///
    /// Se calcula sobre el formato largo y no sobre el ancho a propósito: el ancho colapsa
    /// cada respuesta a un texto legible, y sacar una media de strings obliga a re-parsear
    /// lo que la base ya tenía tipado.
    /// </summary>
    public static class StatsCalculator
    {
        /// <param name="totalResponses">
        /// Fichas de la cohorte. Es el denominador de la completitud, y no se puede deducir
        /// de las filas: una pregunta que nadie respondió no aparece en el resultado.
        /// </param>
        public static ReportStats Compute(ReportResult longResult, ReportSchema schema, int totalResponses)
        {
            var stats = new ReportStats { Responses = totalResponses };

            // Agrupar por pregunta. Una opción múltiple aporta VARIAS filas con el mismo
            // linkId para la misma ficha (una por opción marcada), así que "respondida" se
            // cuenta por ficha distinta y no por fila.
            var byLink = new Dictionary<string, List<Dictionary<string, object>>>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in longResult.Rows ?? new List<Dictionary<string, object>>())
            {
                var link = Convert.ToString(Get(row, "LinkId"));
                if (string.IsNullOrEmpty(link)) continue;
                if (!byLink.TryGetValue(link, out var list)) byLink[link] = list = new List<Dictionary<string, object>>();
                list.Add(row);
            }

            foreach (var col in schema.Columns)
            {
                var tipo = schema.TypeOf(col.Key);
                if (tipo == QuestionType.Attachment) continue;   // un archivo no se resume

                byLink.TryGetValue(col.Key, out var filas);
                filas = filas ?? new List<Dictionary<string, object>>();

                var qs = new QuestionStats
                {
                    LinkId = col.Key,
                    Text = col.Value,
                    Type = tipo.ToString(),
                    Answered = filas.Select(f => Convert.ToString(Get(f, "ResponseId")))
                                    .Where(id => !string.IsNullOrEmpty(id))
                                    .Distinct().Count()
                };
                qs.Missing = Math.Max(0, totalResponses - qs.Answered);
                qs.CompletenessPct = totalResponses > 0
                    ? Math.Round(qs.Answered * 100.0 / totalResponses, 1)
                    : 0;

                if (EsNumerica(tipo)) Numericas(qs, filas);
                else if (EsCategorica(tipo)) Categorias(qs, filas, tipo, schema);

                stats.Questions.Add(qs);
            }

            return stats;
        }

        private static bool EsNumerica(QuestionType t) =>
            t == QuestionType.Integer || t == QuestionType.Decimal || t == QuestionType.Scale;

        private static bool EsCategorica(QuestionType t) =>
            t == QuestionType.Choice || t == QuestionType.OpenChoice || t == QuestionType.Boolean;

        private static void Numericas(QuestionStats qs, List<Dictionary<string, object>> filas)
        {
            var valores = filas
                .Select(f => Numero(Get(f, "ValueDecimal")) ?? Numero(Get(f, "ValueInteger")))
                .Where(v => v.HasValue)
                .Select(v => v.Value)
                .OrderBy(v => v)
                .ToList();

            qs.Kind = "numeric";
            qs.N = valores.Count;
            if (valores.Count == 0) return;

            qs.Min = valores.First();
            qs.Max = valores.Last();
            qs.Mean = Redondear(valores.Average());
            qs.Median = Redondear(Percentil(valores, 0.50));
            qs.P25 = Redondear(Percentil(valores, 0.25));
            qs.P75 = Redondear(Percentil(valores, 0.75));

            // Desvío muestral (n-1). Con una sola observación no está definido: se deja en
            // null en vez de mostrar 0, que se leería como "no hay dispersión".
            if (valores.Count > 1)
            {
                var media = valores.Average();
                var suma = valores.Sum(v => (v - media) * (v - media));
                qs.StdDev = Redondear(Math.Sqrt((double)(suma / (valores.Count - 1))));
            }

            qs.Histogram = Histograma(valores);
        }

        private static void Categorias(QuestionStats qs, List<Dictionary<string, object>> filas,
                                       QuestionType tipo, ReportSchema schema)
        {
            qs.Kind = "categorical";
            var conteos = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var f in filas)
            {
                string etiqueta;
                if (tipo == QuestionType.Boolean)
                {
                    var b = Get(f, "ValueBoolean");
                    if (b == null) continue;
                    etiqueta = Convert.ToDecimal(b) == 1 ? "Sí" : "No";
                }
                else
                {
                    var guardado = Convert.ToString(Get(f, "ValueCode") ?? Get(f, "ValueText"));
                    if (string.IsNullOrWhiteSpace(guardado)) continue;
                    // Distintas respuestas pueden haber guardado el código o el texto de la
                    // misma opción; sin normalizar, la categoría se parte en dos barras.
                    etiqueta = schema.LabelFor(qs.LinkId, guardado);
                }
                conteos[etiqueta] = conteos.TryGetValue(etiqueta, out var n) ? n + 1 : 1;
            }

            // El total de la pregunta, no el de la cohorte: en una opción múltiple la suma de
            // las categorías supera la cantidad de fichas, y un porcentaje sobre la cohorte
            // no cerraría en 100.
            var totalMarcas = conteos.Values.Sum();
            qs.N = totalMarcas;
            qs.Categories = conteos
                .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key)
                .Select(kv => new CategoryCount
                {
                    Label = kv.Key,
                    Count = kv.Value,
                    Pct = totalMarcas > 0 ? Math.Round(kv.Value * 100.0 / totalMarcas, 1) : 0
                })
                .ToList();
        }

        /// <summary>
        /// Histograma de ancho fijo. Sturges (1 + log2 n) acotado a [1, 10]: con pocas
        /// fichas —el caso normal de un servicio— más barras que eso es ruido.
        /// </summary>
        private static List<HistogramBin> Histograma(List<decimal> ordenados)
        {
            var bins = new List<HistogramBin>();
            if (ordenados.Count == 0) return bins;

            var min = ordenados.First();
            var max = ordenados.Last();

            if (min == max)
                return new List<HistogramBin> { new HistogramBin { From = min, To = max, Count = ordenados.Count } };

            var cantidad = (int)Math.Max(1, Math.Min(10, Math.Ceiling(1 + Math.Log(ordenados.Count, 2))));
            var ancho = (max - min) / cantidad;

            for (int i = 0; i < cantidad; i++)
            {
                var desde = min + ancho * i;
                var hasta = i == cantidad - 1 ? max : min + ancho * (i + 1);
                // El último tramo incluye el extremo superior; los demás no, para no contar
                // dos veces un valor que cae justo en el borde.
                var count = i == cantidad - 1
                    ? ordenados.Count(v => v >= desde && v <= hasta)
                    : ordenados.Count(v => v >= desde && v < hasta);
                bins.Add(new HistogramBin { From = Redondear(desde), To = Redondear(hasta), Count = count });
            }
            return bins;
        }

        private static decimal Percentil(List<decimal> ordenados, double p)
        {
            if (ordenados.Count == 1) return ordenados[0];
            // Interpolación lineal entre los dos vecinos: el metodo que usan R (tipo 7),
            // numpy y Excel, para que los numeros coincidan si alguien los recalcula.
            var pos = (ordenados.Count - 1) * p;
            var bajo = (int)Math.Floor(pos);
            var alto = (int)Math.Ceiling(pos);
            if (bajo == alto) return ordenados[bajo];
            var peso = (decimal)(pos - bajo);
            return ordenados[bajo] + (ordenados[alto] - ordenados[bajo]) * peso;
        }

        private static decimal Redondear(decimal v) => Math.Round(v, 2);
        private static decimal Redondear(double v) => Math.Round((decimal)v, 2);

        private static decimal? Numero(object v)
        {
            if (v == null) return null;
            try { return Convert.ToDecimal(v, CultureInfo.InvariantCulture); }
            catch (Exception e) when (e is FormatException || e is InvalidCastException || e is OverflowException)
            {
                return null;
            }
        }

        private static object Get(Dictionary<string, object> row, string key)
        {
            row.TryGetValue(key, out var v);
            return v;
        }
    }

    public class ReportStats
    {
        /// <summary>Fichas de la cohorte: el denominador de toda la pantalla.</summary>
        public int Responses { get; set; }
        public List<QuestionStats> Questions { get; set; } = new List<QuestionStats>();
    }

    public class QuestionStats
    {
        public string LinkId { get; set; }
        public string Text { get; set; }
        public string Type { get; set; }
        /// <summary>numeric | categorical | other — decide qué gráfico dibuja la UI.</summary>
        public string Kind { get; set; } = "other";

        public int Answered { get; set; }
        public int Missing { get; set; }
        public double CompletenessPct { get; set; }

        /// <summary>Observaciones usadas: valores numéricos, o marcas en una categórica.</summary>
        public int N { get; set; }

        public decimal? Min { get; set; }
        public decimal? Max { get; set; }
        public decimal? Mean { get; set; }
        public decimal? Median { get; set; }
        public decimal? StdDev { get; set; }
        public decimal? P25 { get; set; }
        public decimal? P75 { get; set; }
        public List<HistogramBin> Histogram { get; set; }

        public List<CategoryCount> Categories { get; set; }
    }

    public class CategoryCount
    {
        public string Label { get; set; }
        public int Count { get; set; }
        public double Pct { get; set; }
    }

    public class HistogramBin
    {
        public decimal From { get; set; }
        public decimal To { get; set; }
        public int Count { get; set; }
    }
}
