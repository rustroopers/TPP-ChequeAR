using System.Data.Entity;
using ChequeAR.Domain.Entities;
using ChequeAR.Infrastructure.EF.Configurations;

namespace ChequeAR.Infrastructure.EF
{
    public class ChequeARDbContext : DbContext
    {
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
            mb.Configurations.Add(new QuestionnaireConfig());
            mb.Configurations.Add(new QuestionnaireResponseConfig());
            mb.Configurations.Add(new AuditEventConfig());

            mb.Entity<UserRole>().HasKey(x => new { x.UserId, x.Role });
            mb.Entity<UserSpecialty>().HasKey(x => new { x.UserId, x.SpecialtyCode });
            mb.Entity<Specialty>().HasKey(x => x.Code);

            base.OnModelCreating(mb);
        }
    }
}
