using System;
using System.Collections.Generic;
using System.Globalization;

namespace ChequeAR.Application.Reports
{
    /// Transforma el resultado "largo" de vw_FlatResponses (una fila por
    /// respuesta-pregunta) en formato "ancho": una fila por respuesta (encuentro),
    /// una columna por pregunta del cuestionario. Columnas de contexto: HC y Fecha.
    public static class ReportPivot
    {
        private static readonly CultureInfo Ar = CultureInfo.GetCultureInfo("es-AR");

        /// <param name="questionColumns">Pares (linkId, encabezado) en el orden deseado de columnas.</param>
        public static ReportResult ToWide(ReportResult longResult, IList<KeyValuePair<string, string>> questionColumns)
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

                // Valor legible por pregunta (último no nulo por linkId).
                var valueByLink = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var it in items)
                {
                    var link = Convert.ToString(Get(it, "LinkId"), CultureInfo.InvariantCulture);
                    if (string.IsNullOrEmpty(link)) continue;
                    var val = Get(it, "ValueText");
                    if (val != null) valueByLink[link] = val;
                }

                foreach (var qc in questionColumns)
                {
                    valueByLink.TryGetValue(qc.Key, out var v);
                    row[qc.Value] = v;
                }
                rows.Add(row);
            }

            return new ReportResult { Rows = rows, Total = rows.Count };
        }

        private static object Get(Dictionary<string, object> row, string key)
        {
            row.TryGetValue(key, out var v);
            return v;
        }
    }
}
