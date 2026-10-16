using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.ModelConfiguration.Conventions;
using System.Data.Entity.Validation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ChequeAR.Application.Validation;
using ChequeAR.Domain.Entities;
using ChequeAR.Infrastructure.EF.Configurations;

namespace ChequeAR.Infrastructure.EF
{
    public class ChequeARDbContext : DbContext
    {
        // Aplica las migraciones pendientes la primera vez que se usa el contexto, para
        // que un deploy no necesite un paso manual contra la base.
        //
        // Va aca y no en Application_Start por dos razones: ChequeAR.Api no referencia
        // EntityFramework (solo Infrastructure, y en csproj clasico las referencias no son
        // transitivas), y siendo lazy una base inaccesible hace fallar unicamente las
        // pantallas que la tocan en vez de tumbar el arranque entero de la app.
        static ChequeARDbContext()
        {
            Database.SetInitializer(
                new MigrateDatabaseToLatestVersion<ChequeARDbContext, Migrations.Configuration>());
        }

        // ODP.NET no tiene límite por defecto: una espera de lock dejaba el request colgado.
        public const int CommandTimeoutSeconds = 30;

        public ChequeARDbContext() : base("name=ChequeAR") { Database.CommandTimeout = CommandTimeoutSeconds; }
        public ChequeARDbContext(string connectionString) : base(connectionString) { Database.CommandTimeout = CommandTimeoutSeconds; }

        public DbSet<Questionnaire> Questionnaires { get; set; }
        public DbSet<QuestionnaireVersion> QuestionnaireVersions { get; set; }
        public DbSet<QuestionGroup> QuestionGroups { get; set; }
        public DbSet<Question> Questions { get; set; }
        public DbSet<AnswerOption> AnswerOptions { get; set; }
        public DbSet<QuestionnaireResponse> QuestionnaireResponses { get; set; }
        public DbSet<QuestionnaireResponseItem> QuestionnaireResponseItems { get; set; }
        public DbSet<Attachment> Attachments { get; set; }
        public DbSet<AuditEvent> AuditEvents { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<UserRole> UserRoles { get; set; }
        public DbSet<UserSpecialty> UserSpecialties { get; set; }
        public DbSet<Specialty> Specialties { get; set; }
        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<SavedReport> SavedReports { get; set; }
        public DbSet<ReportExecution> ReportExecutions { get; set; }
        public DbSet<ExportArtifact> ExportArtifacts { get; set; }
        public DbSet<ExportJob> ExportJobs { get; set; }
        public DbSet<ClinicalVariable> ClinicalVariables { get; set; }
        public DbSet<VariableOption> VariableOptions { get; set; }
        public DbSet<ClinicalVariableVersion> ClinicalVariableVersions { get; set; }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            // En Oracle el "schema" es el usuario dueño de los objetos.
            modelBuilder.HasDefaultSchema("CHEQUEAR");

            // Tablas = nombre de entidad (singular), no pluralizado: predecible y alineado
            // con el SQL crudo del motor de reportes (ej. "QuestionnaireResponseItem").
            modelBuilder.Conventions.Remove<PluralizingTableNameConvention>();
            // Todas las PK las asigna la app (Guid.NewGuid() o el id de usuario del hospital),
            // ninguna es identity/secuencia generada por la base.
            modelBuilder.Conventions.Remove<StoreGeneratedIdentityKeyConvention>();
            // "User" es palabra reservada en Oracle → tabla AppUser.
            modelBuilder.Entity<User>().ToTable("AppUser");

            // Por defecto, todo string es VARCHAR2 acotado (no unicode → evita NVARCHAR2,
            // la base XE/prod usa AL32UTF8 y soporta acentos en VARCHAR2). Sin acotar, EF
            // mapearía a (N)CLOB, que en Oracle no puede ser PK/FK ni compararse en un WHERE.
            // Los campos JSON/largos se sobreescriben explícitamente a CLOB más abajo.
            modelBuilder.Properties<string>().Configure(p => p.HasMaxLength(2000).IsUnicode(false));

            modelBuilder.Configurations.Add(new QuestionnaireConfig());
            modelBuilder.Configurations.Add(new QuestionnaireVersionConfig());
            modelBuilder.Configurations.Add(new QuestionnaireResponseConfig());
            modelBuilder.Entity<QuestionnaireResponseItem>().Property(x => x.ValueDecimal).HasPrecision(22, ResponseValidator.DecimalPlaces);
            modelBuilder.Configurations.Add(new AuditEventConfig());
            modelBuilder.Configurations.Add(new ClinicalVariableConfig());
            modelBuilder.Configurations.Add(new ClinicalVariableVersionConfig());

            // Sin esto, EF no asocia QuestionGroup.Questions con Question.GroupId (no hay
            // navegación inversa en Question) y crea una FK sombra "QuestionGroup_Id" que
            // no existe en el esquema Oracle -> ORA-00904 al guardar.
            modelBuilder.Entity<QuestionGroup>().HasMany(g => g.Questions).WithOptional().HasForeignKey(q => q.GroupId).WillCascadeOnDelete(false);

            // Mismo motivo que el mapeo de arriba: VariableOption no tiene navegación inversa,
            // así que sin esto EF inventa la FK sombra "ClinicalVariable_Id" -> ORA-00904.
            // WithRequired porque una opción no existe sin su variable, y sin cascade para
            // seguir el criterio del resto del esquema (los borrados son explícitos, en
            // transacción, desde el repositorio).
            modelBuilder.Entity<ClinicalVariable>().HasMany(v => v.Options).WithRequired().HasForeignKey(o => o.VariableId).WillCascadeOnDelete(false);
            modelBuilder.Entity<ClinicalVariable>().HasMany(v => v.Versions).WithRequired().HasForeignKey(x => x.VariableId).WillCascadeOnDelete(false);

            // Claves string: acotadas para que sean PK/FK indexables en Oracle.
            modelBuilder.Entity<User>().Property(x => x.Id).HasMaxLength(128);
            // Formato "algo$iteraciones$salt$hash" en base64 (~95 chars hoy); holgura para
            // subir iteraciones o cambiar de algoritmo sin migrar el esquema de nuevo.
            modelBuilder.Entity<User>().Property(x => x.PasswordHash).HasMaxLength(256);
            modelBuilder.Entity<User>().Property(x => x.SecurityStamp).HasMaxLength(64);
            modelBuilder.Entity<UserRole>().HasKey(x => new { x.UserId, x.Role });
            modelBuilder.Entity<UserRole>().Property(x => x.UserId).HasMaxLength(128);
            modelBuilder.Entity<UserRole>().Property(x => x.Role).HasMaxLength(32);
            modelBuilder.Entity<UserSpecialty>().HasKey(x => new { x.UserId, x.SpecialtyCode });
            modelBuilder.Entity<UserSpecialty>().Property(x => x.UserId).HasMaxLength(128);
            modelBuilder.Entity<UserSpecialty>().Property(x => x.SpecialtyCode).HasMaxLength(64);
            modelBuilder.Entity<Specialty>().HasKey(x => x.Code);
            modelBuilder.Entity<Specialty>().Property(x => x.Code).HasMaxLength(64);
            // Nombre de zona del tema (red|violet|green|yellow|blue|orange), no un color libre.
            modelBuilder.Entity<Specialty>().Property(x => x.Color).HasMaxLength(32);
            modelBuilder.Entity<RefreshToken>().Property(x => x.UserId).HasMaxLength(128);
            // Consistencia con Questionnaire.SpecialtyCode/Specialty.Code (ambos 64): estaba
            // en 2000 por la convención global de strings, un ancho que nunca podría matchear
            // la PK del catálogo. Puede ser NULL (Scope="private" no tiene especialidad).
            modelBuilder.Entity<SavedReport>().Property(x => x.SpecialtyCode).HasMaxLength(64);

            // Campos JSON / texto largo → CLOB. IsMaxLength() suelta el límite de 2000 que
            // impone la convención de arriba: sin esto, EF valida client-side contra 2000
            // caracteres y un FHIR Questionnaire real (mayor a eso) revienta al publicar con
            // "Validation failed for one or more entities", aunque la columna CLOB lo acepte.
            // OJO: HasMaxLength(null) NO alcanza acá — para EF6 equivale a "no configurado",
            // así que la convención de arriba (Properties<string>().Configure) sigue ganando
            // y el límite de 2000 queda puesto igual. Hay que pedir explícitamente el máximo.
            modelBuilder.Entity<QuestionnaireVersion>().Property(x => x.FhirJson).HasColumnType("CLOB").IsMaxLength();
            modelBuilder.Entity<QuestionnaireResponse>().Property(x => x.FhirJson).HasColumnType("CLOB").IsMaxLength();
            modelBuilder.Entity<AuditEvent>().Property(x => x.FhirJson).HasColumnType("CLOB").IsMaxLength();
            modelBuilder.Entity<Question>().Property(x => x.ValidationJson).HasColumnType("CLOB").IsMaxLength();
            modelBuilder.Entity<Question>().Property(x => x.EnableWhenJson).HasColumnType("CLOB").IsMaxLength();
            modelBuilder.Entity<Question>().Property(x => x.CodingJson).HasColumnType("CLOB").IsMaxLength();
            modelBuilder.Entity<Questionnaire>().Property(x => x.CodingJson).HasColumnType("CLOB").IsMaxLength();
            modelBuilder.Entity<SavedReport>().Property(x => x.QueryJson).HasColumnType("CLOB").IsMaxLength();
            modelBuilder.Entity<ReportExecution>().Property(x => x.QueryJson).HasColumnType("CLOB").IsMaxLength();
            modelBuilder.Entity<ExportJob>().Property(x => x.QueryJson).HasColumnType("CLOB").IsMaxLength();

            base.OnModelCreating(modelBuilder);
        }
        // ---- Validacion: hacer legible el DbEntityValidationException ----
        //
        // El Message de esa excepcion es siempre el mismo texto generico ("Validation failed
        // for one or more entities...") y remite a la propiedad EntityValidationErrors, que
        // ningun logger serializa: en el archivo de log queda el stack trace sin una sola
        // pista de que entidad ni que propiedad fallo. Al envolverla, el detalle viaja en el
        // Message y aparece tal cual en Serilog, sin que Api ni Application tengan que
        // referenciar EntityFramework para desarmarla.
        //
        // Se envuelve en vez de reemplazar: la original queda como InnerException, asi que
        // nada que hoy la capture por tipo deja de funcionar.
        public override int SaveChanges()
        {
            try
            {
                return base.SaveChanges();
            }
            catch (DbEntityValidationException ex)
            {
                throw Describe(ex);
            }
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbEntityValidationException ex)
            {
                throw Describe(ex);
            }
        }

        /// Arma "Entidad.Propiedad: mensaje" por cada regla violada, agrupado por entidad.
        private static DbEntityValidationException Describe(DbEntityValidationException ex)
        {
            var detalle = new StringBuilder("Fallo la validacion de EF al guardar. Errores: ");
            var partes = new List<string>();

            foreach (var entry in ex.EntityValidationErrors)
            {
                // GetType().BaseType: con lazy loading EF entrega proxies dinamicos y el
                // nombre del proxy no dice nada ("Questionnaire_A3F2...").
                var tipo = entry.Entry.Entity.GetType();
                var nombre = tipo.Namespace == "System.Data.Entity.DynamicProxies"
                    ? tipo.BaseType.Name
                    : tipo.Name;

                foreach (var error in entry.ValidationErrors)
                {
                    partes.Add(nombre + "." + error.PropertyName + ": " + error.ErrorMessage);
                }
            }

            detalle.Append(partes.Count == 0
                ? "(la excepcion no trajo ningun detalle)"
                : string.Join(" | ", partes));

            return new DbEntityValidationException(detalle.ToString(), ex.EntityValidationErrors, ex);
        }

    }
}
