using System.Data.Entity.Migrations;
using ChequeAR.Infrastructure.EF;

namespace ChequeAR.Infrastructure.Migrations
{
    /// Configuracion de migraciones Code First para el store transaccional
    /// (schema CHEQUEAR en Oracle). El generador de SQL lo resuelve EF a
    /// traves del provider de ODP.NET, no hace falta registrarlo aca.
    ///
    /// Publica (en vez de `internal sealed`, que es lo que genera
    /// Enable-Migrations) para que la herramienta de scaffolding pueda
    /// instanciarla desde fuera del assembly.
    public class Configuration : DbMigrationsConfiguration<ChequeARDbContext>
    {
        public Configuration()
        {
            AutomaticMigrationsEnabled = false;
            AutomaticMigrationDataLossAllowed = false;
            ContextKey = "ChequeAR.Infrastructure.EF.ChequeARDbContext";
        }
    }
}
