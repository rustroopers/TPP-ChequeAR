using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Entity.Migrations;
using System.Diagnostics;
using System.Linq;
using ChequeAR.Application.Security;
using ChequeAR.Application.Services;
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

        private const string SnomedSystem = TerminologyService.SnomedSystem;

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
            SeedClinicalVariables(context);
            if (DemoUsersEnabled()) SeedDemoUsers(context);
            BackfillResponses(context);
        }

        private static void BackfillResponses(ChequeARDbContext context)
        {
            // Lo sembrado hasta acá se guarda antes: el backfill descarta lo pendiente si una ficha falla.
            context.SaveChanges();
            try
            {
                var fichas = ResponseBackfill.Run(context);
                if (fichas > 0) Trace.TraceInformation("Fichas puestas al día: " + fichas + ".");
            }
            catch (Exception ex)
            {
                // No frena el arranque: el servicio normaliza cada ficha al volver a guardarla.
                Trace.TraceError("No se pudieron poner al día las fichas: " + ex);
            }
        }

        /// <summary>
        /// Catálogo mínimo de variables clínicas para que la app arranque usable: sin al menos
        /// una variable, nadie puede crear un cuestionario.
        ///
        /// A diferencia de SeedSpecialties, acá NO se usa AddOrUpdate: existe un ABM de
        /// variables, así que pisar Name/Unit/rango en cada arranque revertiría en silencio lo
        /// que el Administrador editó. Se inserta sólo lo que falta, por Code.
        ///
        /// Los conceptos SNOMED son los canónicos de signos vitales; el resto del catálogo lo
        /// carga el Administrador desde la pantalla, que es el punto de la feature.
        /// </summary>
        private static void SeedClinicalVariables(ChequeARDbContext context)
        {
            var seed = new[]
            {
                Variable("peso", "Peso", "Peso corporal", QuestionType.Decimal, "kg", 0m, 300m, "27113001", "Body weight", 10),
                Variable("talla", "Talla", "Talla / altura", QuestionType.Decimal, "cm", 0m, 250m, "1153637007", "Body height", 20),
                Variable("imc", "IMC", "Índice de masa corporal", QuestionType.Decimal, "kg/m2", 0m, 100m, "60621009", "Body mass index", 30),
                Variable("ta-sistolica", "TA sistólica", "Tensión arterial sistólica", QuestionType.Integer, "mm[Hg]", 0m, 300m, "271649006", "Systolic blood pressure", 40),
                Variable("ta-diastolica", "TA diastólica", "Tensión arterial diastólica", QuestionType.Integer, "mm[Hg]", 0m, 200m, "271650006", "Diastolic blood pressure", 50),
                Variable("frecuencia-cardiaca", "Frecuencia cardíaca", "Frecuencia cardíaca", QuestionType.Integer, "/min", 0m, 300m, "364075005", "Heart rate", 60),
                Variable("temperatura", "Temperatura", "Temperatura corporal", QuestionType.Decimal, "Cel", 25m, 45m, "386725007", "Body temperature", 70),
                Variable("saturacion", "Saturación de O2", "Saturación arterial de oxígeno", QuestionType.Integer, "%", 0m, 100m, "442476006", "Arterial oxygen saturation", 80),
                Variable("dolor-escala", "Dolor (0-10)", "Intensidad del dolor en escala 0 a 10", QuestionType.Scale, null, 0m, 10m, "225908003", "Pain score", 90)
            };

            var missing = seed.Where(v => !context.ClinicalVariables.Any(x => x.Code == v.Code)).ToList();
            foreach (var v in missing) context.ClinicalVariables.Add(v);

            // Una variable de opción única, para que el catálogo de arranque muestre también
            // el caso en que el significado clínico está en la respuesta y no en el enunciado.
            if (!context.ClinicalVariables.Any(x => x.Code == "severidad"))
            {
                var severidad = Variable("severidad", "Severidad", "Severidad del cuadro",
                    QuestionType.Choice, null, null, null, "272141005", "Severities", 100);
                AddOption(severidad, "leve", "Leve", "255604002", "Mild");
                AddOption(severidad, "moderada", "Moderada", "6736007", "Moderate");
                AddOption(severidad, "severa", "Severa", "24484000", "Severe");
                context.ClinicalVariables.Add(severidad);
            }

            context.SaveChanges();
        }

        private static ClinicalVariable Variable(string code, string name, string defaultText,
            QuestionType type, string unit, decimal? min, decimal? max,
            string snomedCode, string snomedDisplay, int sortOrder)
        {
            var now = DateTime.UtcNow;
            return new ClinicalVariable
            {
                Id = Guid.NewGuid(),
                Code = code,
                Name = name,
                DefaultText = defaultText,
                Type = type,
                Unit = unit,
                MinValue = min,
                MaxValue = max,
                SnomedSystem = SnomedSystem,
                SnomedCode = snomedCode,
                SnomedDisplay = snomedDisplay,
                Term = defaultText,
                IsActive = true,
                SortOrder = sortOrder,
                Revision = 1,
                CreatedBy = "seed",
                CreatedAt = now,
                UpdatedBy = "seed",
                UpdatedAt = now
            };
        }

        private static void AddOption(ClinicalVariable v, string value, string display, string snomedCode, string snomedDisplay)
        {
            v.Options.Add(new VariableOption
            {
                Id = Guid.NewGuid(),
                VariableId = v.Id,
                Value = value,
                Display = display,
                // Code == SnomedCode cuando la opción está codificada: R5 admite un solo Coding
                // por answerOption.value, y unificarlos evita que el código que guardan las
                // respuestas (ValueCode, sobre el que pivotean los reportes) difiera del FHIR.
                Code = snomedCode,
                SnomedSystem = SnomedSystem,
                SnomedCode = snomedCode,
                SnomedDisplay = snomedDisplay,
                OrderIndex = v.Options.Count
            });
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
        /// Solo inserta los que faltan, con su rol y especialidades: lo que se cambió desde el
        /// ABM no se pisa.
        /// </summary>
        private static void SeedDemoUsers(ChequeARDbContext context)
        {
            var now = DateTime.UtcNow;
            var demo = new[]
            {
                // (id, nombre, rol, especialidades)
                Tuple.Create("admin.demo", "Admin Demo", Role.Administrador, Array.Empty<string>()),     // ve todo sin especialidades
                Tuple.Create("jefe.general", "Jefe General", Role.Jefe, new[] { "general" }),
                Tuple.Create("jefe.cardio", "Jefe Cardiología", Role.Jefe, new[] { "cardio" }),
                Tuple.Create("jefe.neumo", "Jefe Neumonología", Role.Jefe, new[] { "neumo" }),
                Tuple.Create("jefe.onco", "Jefe Oncología", Role.Jefe, new[] { "onco" }),
                Tuple.Create("jefe.reuma", "Jefe Reumatología", Role.Jefe, new[] { "reuma" }),
                Tuple.Create("med.general", "Médico General", Role.Medico, new[] { "general" }),
                Tuple.Create("med.cardio", "Médico Cardiología", Role.Medico, new[] { "cardio" }),
                Tuple.Create("med.neumo", "Médico Neumonología", Role.Medico, new[] { "neumo" }),
                Tuple.Create("med.onco", "Médico Oncología", Role.Medico, new[] { "onco" }),
                Tuple.Create("med.reuma", "Médico Reumatología", Role.Medico, new[] { "reuma" }),
                Tuple.Create("med.multi", "Médico Multi", Role.Medico, new[] { "cardio", "neumo" }),
                Tuple.Create("med.huerfano", "Médico Huérfano", Role.Medico, Array.Empty<string>())      // fail-closed: no ve nada
            };

            var ids = demo.Select(d => d.Item1).ToList();
            var existentes = new HashSet<string>(
                context.Users.Where(u => ids.Contains(u.Id)).Select(u => u.Id).ToList(),
                StringComparer.OrdinalIgnoreCase);

            var nuevos = demo.Where(d => !existentes.Contains(d.Item1)).ToList();
            foreach (var d in nuevos)
            {
                context.Users.Add(new User { Id = d.Item1, DisplayName = d.Item2, IsActive = true, LastSyncAt = now });
                context.UserRoles.Add(new UserRole { UserId = d.Item1, Role = d.Item3 });
                foreach (var code in d.Item4)
                    context.UserSpecialties.Add(new UserSpecialty { UserId = d.Item1, SpecialtyCode = code });
            }
            if (nuevos.Count > 0) context.SaveChanges();

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
