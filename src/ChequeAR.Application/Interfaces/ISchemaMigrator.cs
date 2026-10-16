using System.Collections.Generic;

namespace ChequeAR.Application.Interfaces
{
    /// <summary>
    /// Migraciones del esquema aplicadas a pedido (paso del deploy), no en el primer request.
    /// </summary>
    public interface ISchemaMigrator
    {
        IReadOnlyList<string> Pending();

        /// <summary>Export del esquema con Data Pump en DATA_PUMP_DIR. Devuelve el nombre del archivo.</summary>
        string Backup(string fileName);

        /// <summary>Aplica las pendientes (y el Seed). Devuelve las que aplicó.</summary>
        IReadOnlyList<string> Apply();
    }
}
