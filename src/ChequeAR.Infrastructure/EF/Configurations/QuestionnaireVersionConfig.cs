using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.Infrastructure.Annotations;
using System.Data.Entity.ModelConfiguration;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Infrastructure.EF.Configurations
{
    public class QuestionnaireVersionConfig : EntityTypeConfiguration<QuestionnaireVersion>
    {
        private const string UniqueVersionIndex = "UX_QuestionnaireVersion_Number";

        public QuestionnaireVersionConfig()
        {
            ToTable("QuestionnaireVersion");
            HasKey(x => x.Id);

            // Índice único (QuestionnaireId, VersionNumber). El número de versión lo calcula
            // la aplicación como Versions.Count + 1, o sea que dos publicaciones simultáneas
            // pueden leer el mismo count y generar dos "v2". La base es el único lugar donde
            // esa carrera se puede cerrar de verdad: la segunda inserción falla.
            Property(x => x.QuestionnaireId)
                .HasColumnAnnotation(IndexAnnotation.AnnotationName,
                    new IndexAnnotation(new IndexAttribute(UniqueVersionIndex, 1) { IsUnique = true }));
            Property(x => x.VersionNumber)
                .HasColumnAnnotation(IndexAnnotation.AnnotationName,
                    new IndexAnnotation(new IndexAttribute(UniqueVersionIndex, 2) { IsUnique = true }));
        }
    }
}
