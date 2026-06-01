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
            HasMany(x => x.Items).WithRequired().HasForeignKey(i => i.ResponseId).WillCascadeOnDelete(false);
        }
    }
}
