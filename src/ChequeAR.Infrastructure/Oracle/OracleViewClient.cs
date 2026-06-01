using System.Collections.Generic;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;

namespace ChequeAR.Infrastructure.Oracle
{
    /// <summary>MVP stub. En producción: Oracle.ManagedDataAccess con queries parametrizadas a vistas read-only del hospital.</summary>
    public class OracleViewClient : IOracleViewClient
    {
        private readonly string _connectionString;

        public OracleViewClient(string connectionString)
        {
            _connectionString = connectionString;
        }

        public Task<IReadOnlyList<IDictionary<string, object>>> QueryAsync(string viewName, IReadOnlyDictionary<string, object> filters)
        {
            // Stub: en producción, abrir OracleConnection, ejecutar
            //   SELECT * FROM {viewName} WHERE ... con parámetros bind ":p1", ":p2"
            // y mapear cada fila a IDictionary<string,object>.
            IReadOnlyList<IDictionary<string, object>> empty = new List<IDictionary<string, object>>();
            return Task.FromResult(empty);
        }
    }
}
