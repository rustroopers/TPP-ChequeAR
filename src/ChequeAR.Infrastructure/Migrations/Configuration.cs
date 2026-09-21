using System;
using System.Configuration;
using System.Data.Entity.Migrations;
using System.Diagnostics;
using System.Linq;
using ChequeAR.Application.Security;
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
        ///  2. Sembrar los 13 usuarios de demo -- en DEV Y EN CLOUD por igual (ver
        ///     DemoUsersEnabled: ya no depende de HospitalAuth:LoginUrl, que era la única
        ///     razón por la que un deploy real no los sembraba).
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
        /// Antes exigía HospitalAuth:LoginUrl vacío (misma señal que ServiceComposition usa
        /// para registrar el mock de login) -- eso era justo lo que impedía sembrar estos
        /// usuarios en un deploy real. El login local (LocalCredentialAuthenticator) es
        /// independiente de esa integración, así que el único interruptor que queda es
        /// Seed:DemoUsers, para poder apagarlo explícitamente en un entorno que no los quiera.
        /// </summary>
        private static bool DemoUsersEnabled()
            => !string.Equals(ConfigurationManager.AppSettings["Seed:DemoUsers"], "false",
                              StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Padrón: 1 admin + 1 jefe y 1 medico por cada una de las 5 especialidades, más
        /// med.multi (dos especialidades) y med.huerfano (ninguna, para probar el
        /// fail-closed de SpecialtyGuard) -- 13 usuarios en total. Corre en dev y en cloud
        /// por igual (ver DemoUsersEnabled).
        ///
        /// AddOrUpdate sobre clave compuesta NO borra filas que ya no estén acá: si alguien
        /// agrega una especialidad a mano para probar, el próximo arranque no se la pisa.
        /// </summary>
        private static void SeedDemoUsers(ChequeARDbContext context)
        {
            var now = DateTime.UtcNow;
            context.Users.AddOrUpdate(u => u.Id,
                new User { Id = "admin.demo", DisplayName = "Admin Demo", IsActive = true, LastSyncAt = now },
                new User { Id = "jefe.general", DisplayName = "Jefe General", IsActive = true, LastSyncAt = now },
                new User { Id = "jefe.cardio", DisplayName = "Jefe Cardiología", IsActive = true, LastSyncAt = now },
                new User { Id = "jefe.neumo", DisplayName = "Jefe Neumonología", IsActive = true, LastSyncAt = now },
                new User { Id = "jefe.onco", DisplayName = "Jefe Oncología", IsActive = true, LastSyncAt = now },
                new User { Id = "jefe.reuma", DisplayName = "Jefe Reumatología", IsActive = true, LastSyncAt = now },
                new User { Id = "med.general", DisplayName = "Médico General", IsActive = true, LastSyncAt = now },
                new User { Id = "med.cardio", DisplayName = "Médico Cardiología", IsActive = true, LastSyncAt = now },
                new User { Id = "med.neumo", DisplayName = "Médico Neumonología", IsActive = true, LastSyncAt = now },
                new User { Id = "med.onco", DisplayName = "Médico Oncología", IsActive = true, LastSyncAt = now },
                new User { Id = "med.reuma", DisplayName = "Médico Reumatología", IsActive = true, LastSyncAt = now },
                new User { Id = "med.multi", DisplayName = "Médico Multi", IsActive = true, LastSyncAt = now },
                new User { Id = "med.huerfano", DisplayName = "Médico Huérfano", IsActive = true, LastSyncAt = now });
            context.SaveChanges();

            context.UserRoles.AddOrUpdate(r => new { r.UserId, r.Role },
                new UserRole { UserId = "admin.demo", Role = Role.Administrador },
                new UserRole { UserId = "jefe.general", Role = Role.Jefe },
                new UserRole { UserId = "jefe.cardio", Role = Role.Jefe },
                new UserRole { UserId = "jefe.neumo", Role = Role.Jefe },
                new UserRole { UserId = "jefe.onco", Role = Role.Jefe },
                new UserRole { UserId = "jefe.reuma", Role = Role.Jefe },
                new UserRole { UserId = "med.general", Role = Role.Medico },
                new UserRole { UserId = "med.cardio", Role = Role.Medico },
                new UserRole { UserId = "med.neumo", Role = Role.Medico },
                new UserRole { UserId = "med.onco", Role = Role.Medico },
                new UserRole { UserId = "med.reuma", Role = Role.Medico },
                new UserRole { UserId = "med.multi", Role = Role.Medico },
                new UserRole { UserId = "med.huerfano", Role = Role.Medico });

            context.UserSpecialties.AddOrUpdate(s => new { s.UserId, s.SpecialtyCode },
                // admin.demo: sin especialidades -- verifica que Administrador ve todo igual.
                new UserSpecialty { UserId = "jefe.general", SpecialtyCode = "general" },
                new UserSpecialty { UserId = "jefe.cardio", SpecialtyCode = "cardio" },
                new UserSpecialty { UserId = "jefe.neumo", SpecialtyCode = "neumo" },
                new UserSpecialty { UserId = "jefe.onco", SpecialtyCode = "onco" },
                new UserSpecialty { UserId = "jefe.reuma", SpecialtyCode = "reuma" },
                new UserSpecialty { UserId = "med.general", SpecialtyCode = "general" },
                new UserSpecialty { UserId = "med.cardio", SpecialtyCode = "cardio" },
                new UserSpecialty { UserId = "med.neumo", SpecialtyCode = "neumo" },
                new UserSpecialty { UserId = "med.onco", SpecialtyCode = "onco" },
                new UserSpecialty { UserId = "med.reuma", SpecialtyCode = "reuma" },
                new UserSpecialty { UserId = "med.multi", SpecialtyCode = "cardio" },
                new UserSpecialty { UserId = "med.multi", SpecialtyCode = "neumo" }
                // med.huerfano: sin especialidades -- fail-closed, no ve nada.
                );
            context.SaveChanges();

            SeedDemoPasswords(context);
        }

        /// <summary>
        /// Separado de SeedDemoUsers a propósito: hashear una contraseña es CPU real
        /// (210.000 iteraciones PBKDF2 x 13 usuarios), así que en el arranque normal esto no
        /// hace nada si ya hay un hash -- sólo corre cuando hace falta (usuario nuevo, o
        /// Seed:ResetDemoPasswords=true para rotar la contraseña de todos).
        ///
        /// Seed:DemoPassword vacío/ausente => se deja PasswordHash=null en todos: los 13
        /// usuarios existen pero NINGUNO puede loguearse (fail-closed). Nunca hay un default
        /// hardcodeado acá -- eso pondría la misma contraseña en todo deploy que no configure
        /// el setting, incluida una instancia pública mal configurada.
        /// </summary>
        private static void SeedDemoPasswords(ChequeARDbContext context)
        {
            var password = ConfigurationManager.AppSettings["Seed:DemoPassword"];
            var reset = string.Equals(ConfigurationManager.AppSettings["Seed:ResetDemoPasswords"], "true",
                StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(password))
            {
                Trace.TraceWarning("Seed:DemoPassword vacio: los usuarios de demo se crearon "
                    + "sin PasswordHash y no pueden loguearse hasta que se configure.");
                return;
            }

            var demoIds = new[]
            {
                "admin.demo",
                "jefe.general", "jefe.cardio", "jefe.neumo", "jefe.onco", "jefe.reuma",
                "med.general", "med.cardio", "med.neumo", "med.onco", "med.reuma",
                "med.multi", "med.huerfano"
            };

            var users = context.Users.Where(u => demoIds.Contains(u.Id)).ToList();
            var now = DateTime.UtcNow;
            var changed = false;
            foreach (var u in users)
            {
                if (!reset && !string.IsNullOrEmpty(u.PasswordHash)) continue;
                u.PasswordHash = PasswordHasher.Hash(password);
                u.PasswordUpdatedAt = now;
                changed = true;
            }
            if (changed) context.SaveChanges();
        }
    }
}
