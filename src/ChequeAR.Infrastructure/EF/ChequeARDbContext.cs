using System.Data.Entity;
using System.Data.Entity.ModelConfiguration.Conventions;
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

        public ChequeARDbContext() : base("name=ChequeAR") { }
        public ChequeARDbContext(string connectionString) : base(connectionString) { }

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

        protected override void OnModelCreating(DbModelBuilder mb)
        {
            // En Oracle el "schema" es el usuario dueño de los objetos.
            mb.HasDefaultSchema("CHEQUEAR");

            // Tablas = nombre de entidad (singular), no pluralizado: predecible y alineado
            // con el SQL crudo del motor de reportes (ej. "QuestionnaireResponseItem").
            mb.Conventions.Remove<PluralizingTableNameConvention>();
            // Todas las PK las asigna la app (Guid.NewGuid() o el id de usuario del hospital),
            // ninguna es identity/secuencia generada por la base.
            mb.Conventions.Remove<StoreGeneratedIdentityKeyConvention>();
            // "User" es palabra reservada en Oracle → tabla AppUser.
            mb.Entity<User>().ToTable("AppUser");

            // Por defecto, todo string es VARCHAR2 acotado (no unicode → evita NVARCHAR2,
            // la base XE/prod usa AL32UTF8 y soporta acentos en VARCHAR2). Sin acotar, EF
            // mapearía a (N)CLOB, que en Oracle no puede ser PK/FK ni compararse en un WHERE.
            // Los campos JSON/largos se sobreescriben explícitamente a CLOB más abajo.
            mb.Properties<string>().Configure(p => p.HasMaxLength(2000).IsUnicode(false));

            mb.Configurations.Add(new QuestionnaireConfig());
            mb.Configurations.Add(new QuestionnaireVersionConfig());
            mb.Configurations.Add(new QuestionnaireResponseConfig());
            mb.Configurations.Add(new AuditEventConfig());

            // Sin esto, EF no asocia QuestionGroup.Questions con Question.GroupId (no hay
            // navegación inversa en Question) y crea una FK sombra "QuestionGroup_Id" que
            // no existe en el esquema Oracle -> ORA-00904 al guardar.
            mb.Entity<QuestionGroup>().HasMany(g => g.Questions).WithOptional().HasForeignKey(q => q.GroupId).WillCascadeOnDelete(false);

            // Claves string: acotadas para que sean PK/FK indexables en Oracle.
            mb.Entity<User>().Property(x => x.Id).HasMaxLength(128);
            mb.Entity<UserRole>().HasKey(x => new { x.UserId, x.Role });
            mb.Entity<UserRole>().Property(x => x.UserId).HasMaxLength(128);
            mb.Entity<UserRole>().Property(x => x.Role).HasMaxLength(32);
            mb.Entity<UserSpecialty>().HasKey(x => new { x.UserId, x.SpecialtyCode });
            mb.Entity<UserSpecialty>().Property(x => x.UserId).HasMaxLength(128);
            mb.Entity<UserSpecialty>().Property(x => x.SpecialtyCode).HasMaxLength(64);
            mb.Entity<Specialty>().HasKey(x => x.Code);
            mb.Entity<Specialty>().Property(x => x.Code).HasMaxLength(64);
            mb.Entity<RefreshToken>().Property(x => x.UserId).HasMaxLength(128);

            // Campos JSON / texto largo → CLOB. IsMaxLength() suelta el límite de 2000 que
            // impone la convención de arriba: sin esto, EF valida client-side contra 2000
            // caracteres y un FHIR Questionnaire real (mayor a eso) revienta al publicar con
            // "Validation failed for one or more entities", aunque la columna CLOB lo acepte.
            // OJO: HasMaxLength(null) NO alcanza acá — para EF6 equivale a "no configurado",
            // así que la convención de arriba (Properties<string>().Configure) sigue ganando
            // y el límite de 2000 queda puesto igual. Hay que pedir explícitamente el máximo.
            mb.Entity<QuestionnaireVersion>().Property(x => x.FhirJson).HasColumnType("CLOB").IsMaxLength();
            mb.Entity<QuestionnaireResponse>().Property(x => x.FhirJson).HasColumnType("CLOB").IsMaxLength();
            mb.Entity<AuditEvent>().Property(x => x.FhirJson).HasColumnType("CLOB").IsMaxLength();
            mb.Entity<Question>().Property(x => x.ValidationJson).HasColumnType("CLOB").IsMaxLength();
            mb.Entity<Question>().Property(x => x.EnableWhenJson).HasColumnType("CLOB").IsMaxLength();
            mb.Entity<Question>().Property(x => x.CodingJson).HasColumnType("CLOB").IsMaxLength();
            mb.Entity<Questionnaire>().Property(x => x.CodingJson).HasColumnType("CLOB").IsMaxLength();
            mb.Entity<SavedReport>().Property(x => x.QueryJson).HasColumnType("CLOB").IsMaxLength();
            mb.Entity<ReportExecution>().Property(x => x.QueryJson).HasColumnType("CLOB").IsMaxLength();
            mb.Entity<ExportJob>().Property(x => x.QueryJson).HasColumnType("CLOB").IsMaxLength();

            base.OnModelCreating(mb);
        }
    }
}
