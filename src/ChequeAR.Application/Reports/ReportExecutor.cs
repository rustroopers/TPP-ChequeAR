using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ChequeAR.Application.Dtos;
using Oracle.ManagedDataAccess.Client;

namespace ChequeAR.Application.Reports
{
    public class ReportExecutor
    {
        private readonly string _connectionString;
        private readonly QueryBuilder _builder;

        public ReportExecutor(string connectionString, QueryBuilder builder)
        {
            _connectionString = connectionString;
            _builder = builder;
        }

        public async Task<ReportResult> ExecuteAsync(ReportQuery q)
        {
            var built = _builder.Build(q);
            var rows = new List<Dictionary<string, object>>();
            using (var conn = new OracleConnection(_connectionString))
            using (var cmd = new OracleCommand(built.Sql, conn))
            {
                // Bindeo por nombre: el QueryBuilder usa marcadores :nombre (no posicionales).
                cmd.BindByName = true;
                cmd.Parameters.AddRange(built.Parameters.ToArray());
                await conn.OpenAsync();
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
            return new ReportResult { Rows = rows, Total = rows.Count };
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
