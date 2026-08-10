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
            // Concurrencia optimista: EF suma ConcurrencyVersion al WHERE de cada UPDATE,
            // así que si otra sesión guardó en el medio la actualización afecta 0 filas y
            // salta DbUpdateConcurrencyException en vez de pisar los cambios ajenos.
            // Oracle no tiene rowversion nativo; el contador lo lleva la aplicación.
            Property(x => x.ConcurrencyVersion).IsConcurrencyToken();
            HasMany(x => x.Versions).WithRequired().HasForeignKey(v => v.QuestionnaireId).WillCascadeOnDelete(false);
            HasMany(x => x.Groups).WithRequired().HasForeignKey(g => g.QuestionnaireId).WillCascadeOnDelete(false);
            HasMany(x => x.Questions).WithRequired().HasForeignKey(q => q.QuestionnaireId).WillCascadeOnDelete(false);
        }
    }
}
