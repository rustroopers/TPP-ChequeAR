using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using Oracle.ManagedDataAccess.Client;

namespace ChequeAR.Infrastructure.Oracle
{
    /// <summary>
    /// Cliente read-only contra las vistas autorizadas del hospital (Oracle).
    /// Compatible con cualquier edición/versión de Oracle 12c+ (en dev: XE / XEPDB1).
    ///
    /// Seguridad: el nombre de vista NO es parametrizable como bind variable, así que se
    /// valida contra una allowlist (o, si no se configura, contra un patrón estricto
    /// VW_*). Los filtros sí van como bind variables por nombre (:p0, :p1, ...).
    /// </summary>
    public class OracleViewClient : IOracleViewClient
    {
        // Identificador Oracle válido, opcionalmente calificado por schema (SCHEMA.VW_NOMBRE).
        private static readonly Regex IdentifierPattern =
            new Regex(@"^[A-Za-z][A-Za-z0-9_$#]{0,29}(\.[A-Za-z][A-Za-z0-9_$#]{0,29})?$", RegexOptions.Compiled);

        private readonly string _connectionString;
        private readonly HashSet<string> _allowedViews;

        /// <param name="allowedViews">
        /// Vistas permitidas (case-insensitive). Si es null/vacío, se acepta cualquier
        /// identificador que matchee el patrón y empiece con "VW_".
        /// </param>
        public OracleViewClient(string connectionString, IEnumerable<string> allowedViews = null)
        {
            _connectionString = connectionString;
            _allowedViews = allowedViews != null
                ? new HashSet<string>(allowedViews, StringComparer.OrdinalIgnoreCase)
                : null;
        }

        public async Task<IReadOnlyList<IDictionary<string, object>>> QueryAsync(
            string viewName, IReadOnlyDictionary<string, object> filters)
        {
            var safeView = ValidateViewName(viewName);

            var sql = new StringBuilder("SELECT * FROM ").Append(safeView);
            var binds = new List<OracleParameter>();

            if (filters != null && filters.Count > 0)
            {
                sql.Append(" WHERE ");
                bool first = true;
                int i = 0;
                foreach (var kv in filters)
                {
                    var column = ValidateColumnName(kv.Key);
                    if (!first) sql.Append(" AND ");
                    first = false;
                    var bindName = "p" + i;
                    sql.Append(column).Append(" = :").Append(bindName);
                    binds.Add(new OracleParameter(bindName, kv.Value ?? DBNull.Value));
                    i++;
                }
            }

            var rows = new List<IDictionary<string, object>>();
            using (var conn = new OracleConnection(_connectionString))
            using (var cmd = new OracleCommand(sql.ToString(), conn))
            {
                cmd.BindByName = true;
                cmd.CommandType = CommandType.Text;
                cmd.Parameters.AddRange(binds.ToArray());
                await conn.OpenAsync().ConfigureAwait(false);
                using (var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                {
                    while (await reader.ReadAsync().ConfigureAwait(false))
                    {
                        var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                        for (int c = 0; c < reader.FieldCount; c++)
                        {
                            row[reader.GetName(c)] = reader.IsDBNull(c) ? null : reader.GetValue(c);
                        }
                        rows.Add(row);
                    }
                }
            }
            return rows;
        }

        private string ValidateViewName(string viewName)
        {
            if (string.IsNullOrWhiteSpace(viewName) || !IdentifierPattern.IsMatch(viewName))
                throw new ArgumentException("Nombre de vista inválido: " + viewName, nameof(viewName));

            if (_allowedViews != null)
            {
                if (!_allowedViews.Contains(viewName))
                    throw new ArgumentException("Vista no autorizada: " + viewName, nameof(viewName));
            }
            else
            {
                // Sin allowlist explícita: limitar a objetos que sean claramente vistas.
                var leaf = viewName.Contains(".") ? viewName.Substring(viewName.IndexOf('.') + 1) : viewName;
                if (!leaf.StartsWith("VW_", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Vista no autorizada (esperado prefijo VW_): " + viewName, nameof(viewName));
            }
            return viewName;
        }

        private static string ValidateColumnName(string column)
        {
            if (string.IsNullOrWhiteSpace(column) || !IdentifierPattern.IsMatch(column) || column.Contains("."))
                throw new ArgumentException("Nombre de columna inválido: " + column, nameof(column));
            return column;
        }
    }
}
