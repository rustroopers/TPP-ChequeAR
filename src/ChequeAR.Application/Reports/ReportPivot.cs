using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ChequeAR.Application.Reports
{
    /// Transforma el resultado "largo" de vw_FlatResponses (una fila por
    /// respuesta-pregunta) en formato "ancho": una fila por respuesta (encuentro),
    /// una columna por pregunta del cuestionario. Columnas de contexto: HC y Fecha.
    public static class ReportPivot
    {
        private static readonly CultureInfo Ar = CultureInfo.GetCultureInfo("es-AR");
        private static readonly string[] ContextColumns = { "HC", "Fecha" };
        private const string MultiValueSeparator = " | ";

        /// <param name="questionColumns">Pares (linkId, encabezado) en el orden deseado de columnas.</param>
        /// <param name="label">(linkId, valor guardado) -> texto a mostrar; traduce el código de una opción a su etiqueta.</param>
        public static ReportResult ToWide(ReportResult longResult, IList<KeyValuePair<string, string>> questionColumns,
                                          Func<string, string, string> label = null)
        {
            var order = new List<string>();
            var byResponse = new Dictionary<string, List<Dictionary<string, object>>>();

            foreach (var r in longResult.Rows)
            {
                var rid = Convert.ToString(Get(r, "ResponseId"), CultureInfo.InvariantCulture);
                if (!byResponse.TryGetValue(rid, out var list))
                {
                    list = new List<Dictionary<string, object>>();
                    byResponse[rid] = list;
                    order.Add(rid);
                }
                list.Add(r);
            }

            var headers = UniqueHeaders(questionColumns);

            var rows = new List<Dictionary<string, object>>();
            foreach (var rid in order)
            {
                var items = byResponse[rid];
                var first = items[0];

                var row = new Dictionary<string, object>();
                row["HC"] = Get(first, "PatientHospitalId");
                var authored = Get(first, "AuthoredAt");
                // AuthoredAt está en UTC: sin convertir, una ficha de las 22 hs sale con la
                // fecha del día siguiente.
                row["Fecha"] = authored is DateTime dt ? ArgentinaClock.FromUtc(dt).ToString("yyyy-MM-dd", Ar) : authored;

                // Valores legibles por pregunta. Una opción múltiple trae una fila por opción
                // marcada: se conservan todas, en orden, y se unen en la celda.
                var valuesByLink = new Dictionary<string, List<object>>(StringComparer.OrdinalIgnoreCase);
                foreach (var it in items)
                {
                    var link = Convert.ToString(Get(it, "LinkId"), CultureInfo.InvariantCulture);
                    if (string.IsNullOrEmpty(link)) continue;
                    // Un número va como número: el exportador lo formatea (coma en el CSV, celda
                    // numérica en el XLSX). ValueText lo trae con punto.
                    var val = Get(it, "ValueDecimal") ?? Get(it, "ValueInteger") ?? Get(it, "ValueText");
                    if (val == null) continue;
                    if (label != null && val is string text) val = label(link, text);
                    if (!valuesByLink.TryGetValue(link, out var vals)) valuesByLink[link] = vals = new List<object>();
                    if (!vals.Contains(val)) vals.Add(val);
                }

                for (int i = 0; i < questionColumns.Count; i++)
                {
                    valuesByLink.TryGetValue(questionColumns[i].Key, out var vals);
                    row[headers[i]] = vals == null ? null
                        : vals.Count == 1 ? vals[0]
                        : string.Join(MultiValueSeparator, vals.Select(v => Convert.ToString(v, CultureInfo.InvariantCulture)));
                }
                rows.Add(row);
            }

            return new ReportResult { Rows = rows, Total = rows.Count };
        }

        /// <summary>
        /// Encabezado de cada pregunta, sin repetidos. La fila es un diccionario por
        /// encabezado: dos preguntas con el mismo texto (o una llamada "HC" o "Fecha") se
        /// pisaban entre sí y una columna desaparecía. La repetida se desambigua con su linkId.
        /// </summary>
        private static List<string> UniqueHeaders(IList<KeyValuePair<string, string>> questionColumns)
        {
            var usados = new HashSet<string>(ContextColumns, StringComparer.OrdinalIgnoreCase);
            var headers = new List<string>(questionColumns.Count);
            foreach (var qc in questionColumns)
            {
                var texto = string.IsNullOrWhiteSpace(qc.Value) ? qc.Key : qc.Value;
                var header = texto;
                if (usados.Contains(header)) header = texto + " (" + qc.Key + ")";
                for (int n = 2; usados.Contains(header); n++) header = texto + " (" + qc.Key + " " + n + ")";
                usados.Add(header);
                headers.Add(header);
            }
            return headers;
        }

        private static object Get(Dictionary<string, object> row, string key)
        {
            row.TryGetValue(key, out var v);
            return v;
        }
    }
}
