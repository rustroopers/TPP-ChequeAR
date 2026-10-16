using System.Collections.Generic;
using System.Threading.Tasks;

namespace ChequeAR.Application.Interfaces
{
    public interface IOracleViewClient
    {
        Task<IReadOnlyList<IDictionary<string, object>>> QueryAsync(
            string viewName,
            IReadOnlyDictionary<string, object> filters);

        /// Busqueda por texto libre: matchea <paramref name="term"/> contra
        /// cualquiera de <paramref name="columns"/> (OR, case-insensitive,
        /// substring). Pensada para el buscador de pacientes, donde el usuario
        /// tipea indistintamente documento, id o apellido.
        Task<IReadOnlyList<IDictionary<string, object>>> SearchAsync(
            string viewName,
            IEnumerable<string> columns,
            string term,
            int maxRows = 50);

        /// <summary>Comprueba la conexión, fuera de la allowlist de vistas.</summary>
        Task PingAsync();
    }
}
