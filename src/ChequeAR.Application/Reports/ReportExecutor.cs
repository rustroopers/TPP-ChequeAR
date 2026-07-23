using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ChequeAR.Application.Dtos;
using ChequeAR.Application.Interfaces;
using Oracle.ManagedDataAccess.Client;

namespace ChequeAR.Application.Reports
{
    public class ReportExecutor
    {
        /// <summary>
        /// Tope duro de la exportación sincrónica. FR-006 manda a background lo que pase de
        /// 10k filas; mientras ese camino no exista, es preferible cortar con un error
        /// entendible que tumbar el proceso armando el archivo en memoria.
        /// </summary>
        public const int MaxSyncRows = 10000;

        private readonly string _connectionString;
        private readonly QueryBuilder _builder;
        private readonly IQuestionnaireRepository _questionnaires;

        public ReportExecutor(string connectionString, QueryBuilder builder, IQuestionnaireRepository questionnaires)
        {
            _connectionString = connectionString;
            _builder = builder;
            _questionnaires = questionnaires;
        }

        /// <summary>
        /// Una página de la cohorte, ya pivoteada a formato ancho (una fila por ficha).
        /// Devuelve el total REAL de fichas que matchean, no el de la página.
        /// </summary>
        public Task<ReportResult> ExecuteAsync(ReportQuery q) => RunAsync(q, paginate: true);

        /// <summary>La cohorte completa, para exportar. Sin paginar, con tope.</summary>
        public Task<ReportResult> ExecuteAllAsync(ReportQuery q) => RunAsync(q, paginate: false);

        /// <summary>
        /// Estadísticas descriptivas de la cohorte (FR-008). Va sin paginar: una media
        /// calculada sobre la primera página no es la media de la cohorte.
        /// </summary>
        public async Task<ReportStats> ExecuteStatsAsync(ReportQuery q)
        {
            var raw = await RunRawAsync(q, paginate: false);
            return StatsCalculator.Compute(
                new ReportResult { Rows = raw.Rows, Total = raw.Rows.Count },
                raw.Schema,
                raw.TotalResponses);
        }

        private async Task<ReportResult> RunAsync(ReportQuery q, bool paginate)
        {
            var raw = await RunRawAsync(q, paginate);
            var wide = ReportPivot.ToWide(
                new ReportResult { Rows = raw.Rows, Total = raw.Rows.Count }, raw.Schema.Columns);
            // El total de la cohorte, no el de la página traída.
            wide.Total = raw.TotalResponses;
            return wide;
        }

        /// <summary>Filas en formato largo + el total real de fichas + la metadata usada.</summary>
        private async Task<RawResult> RunRawAsync(ReportQuery q, bool paginate)
        {
            // La metadata del cuestionario decide de qué tipo es cada pregunta (y por lo
            // tanto qué columna consultar) y qué columnas tiene el reporte. Es lo que hace
            // que el pivot y el SQL no puedan quedar desalineados: salen de la misma fuente.
            var questionnaire = await _questionnaires.GetAsync(q.QuestionnaireId);
            if (questionnaire == null)
                throw new InvalidOperationException("El cuestionario del reporte no existe.");
            var schema = ReportSchema.From(questionnaire);

            var built = _builder.Build(q, schema, paginate);

            var rows = new List<Dictionary<string, object>>();
            int totalResponses;

            using (var conn = new OracleConnection(_connectionString))
            {
                await conn.OpenAsync();

                using (var countCmd = new OracleCommand(built.CountSql, conn))
                {
                    countCmd.BindByName = true;
                    countCmd.Parameters.AddRange(CloneParameters(built.Parameters));
                    totalResponses = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
                }

                if (!paginate && totalResponses > MaxSyncRows)
                    throw new InvalidOperationException(
                        "La consulta devuelve " + totalResponses + " fichas y el máximo para exportar en línea es " +
                        MaxSyncRows + ". Acotá el rango de fechas o agregá filtros.");

                using (var cmd = new OracleCommand(built.Sql, conn))
                {
                    // Bindeo por nombre: el QueryBuilder usa marcadores :nombre (no posicionales).
                    cmd.BindByName = true;
                    cmd.Parameters.AddRange(built.Parameters.ToArray());
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var row = new Dictionary<string, object>();
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : OracleValue.Normalize(reader.GetValue(i));
                            }
                            rows.Add(row);
                        }
                    }
                }
            }

            return new RawResult { Rows = rows, TotalResponses = totalResponses, Schema = schema };
        }

        private class RawResult
        {
            public List<Dictionary<string, object>> Rows { get; set; }
            public int TotalResponses { get; set; }
            public ReportSchema Schema { get; set; }
        }

        /// <summary>
        /// Un OracleParameter no se puede compartir entre dos OracleCommand: al asignarlo al
        /// segundo, ODP.NET tira "El parámetro ya está contenido en otra colección".
        /// </summary>
        private static OracleParameter[] CloneParameters(IEnumerable<OracleParameter> source)
        {
            var copies = new List<OracleParameter>();
            foreach (var p in source)
            {
                copies.Add(new OracleParameter(p.ParameterName, p.OracleDbType, p.Size, p.Value, p.Direction));
            }
            return copies.ToArray();
        }
    }

    public static class OracleValue
    {
        /// <summary>
        /// Los Guid se guardan como RAW(16) y ODP.NET los devuelve como byte[].
        /// Convert.ToString(byte[]) da "System.Byte[]" para TODAS las filas: el pivot
        /// agrupa por ResponseId, así que todas las fichas colapsaban en una sola con las
        /// respuestas mezcladas entre pacientes. Y en el Excel se exportaba ese literal.
        /// </summary>
        public static object Normalize(object value)
        {
            if (value is byte[] bytes && bytes.Length == 16) return new Guid(bytes);
            return value;
        }
    }

    public class ReportResult
    {
        public int Total { get; set; }
        public IReadOnlyList<Dictionary<string, object>> Rows { get; set; }
    }
}
