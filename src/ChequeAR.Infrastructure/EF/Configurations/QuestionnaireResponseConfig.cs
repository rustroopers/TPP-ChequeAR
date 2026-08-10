using System.Data.Entity.ModelConfiguration;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Infrastructure.EF.Configurations
{
    public class QuestionnaireResponseConfig : EntityTypeConfiguration<QuestionnaireResponse>
    {
        public QuestionnaireResponseConfig()
        {
            ToTable("QuestionnaireResponse");
            HasKey(x => x.Id);
            Property(x => x.PatientHospitalId).IsRequired().HasMaxLength(64);
            Property(x => x.AuthorUserId).HasMaxLength(128);
            // Concurrencia optimista, mismo criterio que QuestionnaireConfig: EF suma esta
            // columna al WHERE del UPDATE y tira DbUpdateConcurrencyException si alguien
            // más guardó en el medio.
            Property(x => x.ConcurrencyVersion).IsConcurrencyToken();
            Property(x => x.LockedByUserId).HasMaxLength(128);
            // timestamp y no DATE: Oracle trunca DATE a segundos, y acá se compara contra
            // "ahora" para decidir si el lock venció (mismo motivo que AuditEvent.Recorded).
            Property(x => x.LockedAt).HasColumnType("timestamp");
            Property(x => x.LockExpiresAt).HasColumnType("timestamp");
            HasMany(x => x.Items).WithRequired().HasForeignKey(i => i.ResponseId).WillCascadeOnDelete(false);
        }
    }
}
