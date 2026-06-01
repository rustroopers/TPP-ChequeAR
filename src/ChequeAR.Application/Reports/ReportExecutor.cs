using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading.Tasks;
using ChequeAR.Application.Dtos;

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
            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(built.Sql, conn))
            {
                cmd.Parameters.AddRange(built.Parameters.ToArray());
                await conn.OpenAsync();
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var row = new Dictionary<string, object>();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        }
                        rows.Add(row);
                    }
                }
            }
            return new ReportResult { Rows = rows, Total = rows.Count };
        }
    }

    public class ReportResult
    {
        public int Total { get; set; }
        public IReadOnlyList<Dictionary<string, object>> Rows { get; set; }
    }
}
