using System.Collections.Generic;
using System.Threading.Tasks;
using ChequeAR.Application.Reports;

namespace ChequeAR.Application.Interfaces
{
    /// <summary>
    /// Ejecuta una consulta de reporte contra la base. Es el puerto que permite que la capa
    /// de negocio arme la consulta sin conocer el motor: quién abre la conexión, cómo se
    /// bindean los parámetros y cómo se normaliza lo leído son decisiones del adaptador.
    /// </summary>
    public interface IReportQueryRunner
    {
        Task<QueryRunResult> RunAsync(BuiltQuery query, bool paginate);
    }

    public class QueryRunResult
    {
        /// <summary>Filas en formato largo, tal como las devuelve la consulta.</summary>
        public List<Dictionary<string, object>> Rows { get; set; } = new List<Dictionary<string, object>>();

        /// <summary>
        /// Total REAL de fichas que matchean, no el de la página. Es lo que hace que la
        /// paginación del cliente y las estadísticas hablen de la misma cohorte.
        /// </summary>
        public int TotalResponses { get; set; }
    }
}
