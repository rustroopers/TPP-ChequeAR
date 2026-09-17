using System.Configuration;
using System.Data.Entity.Migrations;
using System.Linq;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
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

        /// <summary>
        /// Corre en CADA arranque (después de aplicar las migraciones pendientes), así que
        /// tiene que ser idempotente. Dos responsabilidades:
        ///  1. Mantener Name/Color/SortOrder del catálogo al día (el alta de las 5 filas
        ///     iniciales vive en la migración AddSpecialtyCatalogColumns, no acá: Seed()
        ///     corre DESPUÉS de todas las migraciones, y para cuando la FK de la migración
        ///     siguiente se crea, el catálogo ya tiene que tener esas filas).
        ///  2. Sembrar usuarios de demo, SOLO en el mismo escenario en que se registra
        ///     MockHospitalAuthClient (HospitalAuth:LoginUrl vacío) -- así no hay dos
        ///     interruptores que puedan quedar desalineados, y un deploy real (con
        ///     HospitalAuth:LoginUrl configurado) jamás siembra un usuario falso.
        /// </summary>
        protected override void Seed(ChequeARDbContext context)
        {
            SeedSpecialties(context);
            if (DemoUsersEnabled()) SeedDemoUsers(context);
        }

        private static void SeedSpecialties(ChequeARDbContext context)
        {
            // AddOrUpdate pisa Name/Color/SortOrder en cada arranque: correcto hoy (no hay
            // ABM de especialidades). El día que exista una pantalla de administración, esto
            // tiene que pasar a "insertar sólo si no existe" (Any(s => s.Code == ...) antes),
            // o un admin que edite el nombre a mano lo vería revertido en el próximo deploy.
            context.Specialties.AddOrUpdate(s => s.Code,
                new Specialty { Code = "general", Name = "General", IsActive = true, Color = "green", SortOrder = 0, IsDefault = true },
                new Specialty { Code = "cardio", Name = "Cardiología", IsActive = true, Color = "red", SortOrder = 10, IsDefault = false },
                new Specialty { Code = "neumo", Name = "Neumonología", IsActive = true, Color = "blue", SortOrder = 20, IsDefault = false },
                new Specialty { Code = "onco", Name = "Oncología", IsActive = true, Color = "violet", SortOrder = 30, IsDefault = false },
                new Specialty { Code = "reuma", Name = "Reumatología", IsActive = true, Color = "yellow", SortOrder = 40, IsDefault = false });
            context.SaveChanges();
        }

        /// <summary>
        /// Misma señal que ServiceComposition usa para registrar MockHospitalAuthClient:
        /// sin HospitalAuth:LoginUrl no hay login real que llamar. Seed:DemoUsers=false
        /// permite apagarlo igual (p. ej. una demo sin hospital pero sin querer usuarios
        /// de mentira).
        /// </summary>
        private static bool DemoUsersEnabled()
        {
            var loginUrl = ConfigurationManager.AppSettings["HospitalAuth:LoginUrl"];
            if (!string.IsNullOrWhiteSpace(loginUrl)) return false;
            return !string.Equals(ConfigurationManager.AppSettings["Seed:DemoUsers"], "false",
                                  System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Padrón mínimo para probar el enforcement por especialidad de punta a punta sin
        /// preparar nada a mano (ver specs/011, verificación end-to-end). AddOrUpdate sobre
        /// clave compuesta NO borra filas que ya no estén acá: si alguien agrega una
        /// especialidad a mano para probar, el próximo arranque no se la pisa.
        /// </summary>
        private static void SeedDemoUsers(ChequeARDbContext context)
        {
            context.Users.AddOrUpdate(u => u.Id,
                new User { Id = "admin.demo", DisplayName = "Admin Demo", IsActive = true, LastSyncAt = System.DateTime.UtcNow },
                new User { Id = "jefe.cardio", DisplayName = "Jefe Cardio", IsActive = true, LastSyncAt = System.DateTime.UtcNow },
                new User { Id = "med.cardio", DisplayName = "Médico Cardio", IsActive = true, LastSyncAt = System.DateTime.UtcNow },
                new User { Id = "med.neumo", DisplayName = "Médico Neumo", IsActive = true, LastSyncAt = System.DateTime.UtcNow },
                new User { Id = "med.multi", DisplayName = "Médico Multi", IsActive = true, LastSyncAt = System.DateTime.UtcNow },
                new User { Id = "obs.general", DisplayName = "Observador General", IsActive = true, LastSyncAt = System.DateTime.UtcNow },
                new User { Id = "med.huerfano", DisplayName = "Médico Huérfano", IsActive = true, LastSyncAt = System.DateTime.UtcNow });
            context.SaveChanges();

            context.UserRoles.AddOrUpdate(r => new { r.UserId, r.Role },
                new UserRole { UserId = "admin.demo", Role = Role.Administrador },
                new UserRole { UserId = "admin.demo", Role = Role.Jefe },
                new UserRole { UserId = "admin.demo", Role = Role.Medico },
                new UserRole { UserId = "jefe.cardio", Role = Role.Jefe },
                new UserRole { UserId = "jefe.cardio", Role = Role.Medico },
                new UserRole { UserId = "med.cardio", Role = Role.Medico },
                new UserRole { UserId = "med.neumo", Role = Role.Medico },
                new UserRole { UserId = "med.multi", Role = Role.Medico },
                new UserRole { UserId = "obs.general", Role = Role.Observador },
                new UserRole { UserId = "med.huerfano", Role = Role.Medico });

            context.UserSpecialties.AddOrUpdate(s => new { s.UserId, s.SpecialtyCode },
                // admin.demo: sin especialidades -- verifica que Administrador ve todo igual.
                new UserSpecialty { UserId = "jefe.cardio", SpecialtyCode = "cardio" },
                new UserSpecialty { UserId = "med.cardio", SpecialtyCode = "cardio" },
                new UserSpecialty { UserId = "med.neumo", SpecialtyCode = "neumo" },
                new UserSpecialty { UserId = "med.multi", SpecialtyCode = "cardio" },
                new UserSpecialty { UserId = "med.multi", SpecialtyCode = "neumo" },
                new UserSpecialty { UserId = "obs.general", SpecialtyCode = "general" }
                // med.huerfano: sin especialidades -- fail-closed, no ve nada.
                );
            context.SaveChanges();
        }
    }
}
