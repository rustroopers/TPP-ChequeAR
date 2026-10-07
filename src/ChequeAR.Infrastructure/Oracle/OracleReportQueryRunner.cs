using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Reports;
using Oracle.ManagedDataAccess.Client;

namespace ChequeAR.Infrastructure.Oracle
{
    /// <summary>
    /// Adaptador de ejecución de reportes contra Oracle.
    ///
    /// Concentra TODO lo que es específico del driver y que antes vivía dentro de
    /// ReportExecutor, en la capa de negocio: la conexión, los comandos, el bindeo por nombre,
    /// la traducción del parámetro neutro al del driver, el clonado entre comandos y la
    /// normalización de lo leído.
    ///
    /// Cambiar de motor o de versión de driver toca este archivo y ninguno más.
    /// </summary>
    public class OracleReportQueryRunner : IReportQueryRunner
    {
        // El driver no corta solo (0 = sin límite): una consulta pesada dejaba el request
        // colgado indefinidamente.
        private const int CommandTimeoutSeconds = 60;

        private readonly string _connectionString;

        public OracleReportQueryRunner(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<QueryRunResult> RunAsync(BuiltQuery query, int? maxResponses)
        {
            var rows = new List<Dictionary<string, object>>();
            int total;

            using (var conn = new OracleConnection(_connectionString))
            {
                await conn.OpenAsync();

                using (var countCmd = new OracleCommand(query.CountSql, conn))
                {
                    countCmd.BindByName = true;
                    countCmd.CommandTimeout = CommandTimeoutSeconds;
                    countCmd.Parameters.AddRange(Traducir(query.Parameters));
                    total = Convert.ToInt32(await countCmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
                }

                if (maxResponses.HasValue && total > maxResponses.Value)
                    return new QueryRunResult { Rows = rows, TotalResponses = total };

                using (var cmd = new OracleCommand(query.Sql, conn))
                {
                    // Bindeo por nombre: el constructor de consultas usa marcadores :nombre,
                    // no posicionales.
                    cmd.BindByName = true;
                    cmd.CommandTimeout = CommandTimeoutSeconds;
                    // Parámetros NUEVOS y no los mismos del conteo: un OracleParameter no se
                    // puede compartir entre dos OracleCommand — al asignarlo al segundo,
                    // ODP.NET tira "El parámetro ya está contenido en otra colección".
                    cmd.Parameters.AddRange(Traducir(query.Parameters));

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var row = new Dictionary<string, object>();
                            for (int i = 0; i < reader.FieldCount; i++)
                                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : Normalizar(reader.GetValue(i));
                            rows.Add(row);
                        }
                    }
                }
            }

            return new QueryRunResult { Rows = rows, TotalResponses = total };
        }

        /// <summary>Parámetro neutro del negocio → parámetro del driver.</summary>
        private static OracleParameter[] Traducir(IEnumerable<QueryParameter> parametros)
        {
            return (parametros ?? Enumerable.Empty<QueryParameter>())
                .Select(Traducir)
                .ToArray();
        }

        private static OracleParameter Traducir(QueryParameter p)
        {
            switch (p.Type)
            {
                case QueryParameterType.Int32:
                    return new OracleParameter(p.Name, OracleDbType.Int32, p.Value, ParameterDirection.Input);

                case QueryParameterType.Raw16:
                    // El identificador está guardado como RAW(16). El driver no sabe bindear
                    // un Guid: tira ArgumentException ("El valor no está dentro del intervalo
                    // esperado") y toda consulta de reportes fallaba antes de llegar a la base.
                    return new OracleParameter(p.Name, OracleDbType.Raw, 16, p.Value, ParameterDirection.Input);

                case QueryParameterType.Date:
                    return new OracleParameter(p.Name, OracleDbType.Date, p.Value, ParameterDirection.Input);

                default:
                    return new OracleParameter(p.Name, p.Value ?? DBNull.Value);
            }
        }

        /// <summary>
        /// Los identificadores se guardan como RAW(16) y el driver los devuelve como byte[].
        /// Convert.ToString(byte[]) da "System.Byte[]" para TODAS las filas: el pivot agrupa
        /// por identificador de ficha, así que todas colapsaban en una sola con las respuestas
        /// mezcladas entre pacientes. Y en el Excel se exportaba ese literal.
        /// </summary>
        public static object Normalizar(object value)
        {
            if (value is byte[] bytes && bytes.Length == 16) return new Guid(bytes);
            return value;
        }
    }
}
