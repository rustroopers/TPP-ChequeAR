using System.Data.Entity.ModelConfiguration;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Infrastructure.EF.Configurations
{
    public class QuestionnaireConfig : EntityTypeConfiguration<Questionnaire>
    {
        public QuestionnaireConfig()
        {
            ToTable("Questionnaire");
            HasKey(x => x.Id);
            Property(x => x.Title).IsRequired().HasMaxLength(255);
            Property(x => x.SpecialtyCode).HasMaxLength(64);
            Property(x => x.CreatedBy).HasMaxLength(128);
            HasMany(x => x.Versions).WithRequired().HasForeignKey(v => v.QuestionnaireId).WillCascadeOnDelete(false);
            HasMany(x => x.Groups).WithRequired().HasForeignKey(g => g.QuestionnaireId).WillCascadeOnDelete(false);
            HasMany(x => x.Questions).WithRequired().HasForeignKey(q => q.QuestionnaireId).WillCascadeOnDelete(false);
        }
    }
}
