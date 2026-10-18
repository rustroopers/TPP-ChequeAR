using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.Infrastructure.Annotations;
using System.Data.Entity.ModelConfiguration;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Infrastructure.EF.Configurations
{
    /// <summary>
    /// Mapeo del catálogo de variables clínicas. Acá se acotan a mano los anchos que la
    /// convención global de ChequeARDbContext mandaría a VARCHAR2(2000): en Oracle una columna
    /// de ese ancho no entra en un índice único, y tanto Code como SnomedCode lo necesitan.
    /// </summary>
    public class ClinicalVariableConfig : EntityTypeConfiguration<ClinicalVariable>
    {
        public ClinicalVariableConfig()
        {
            ToTable("ClinicalVariable");
            HasKey(x => x.Id);

            // Code: clave de negocio. Único incluso entre variables dadas de baja, para que
            // reactivar "peso" no choque con un "peso" nuevo creado mientras tanto.
            Property(x => x.Code)
                .IsRequired()
                .HasMaxLength(64)
                .HasColumnAnnotation(IndexAnnotation.AnnotationName,
                    new IndexAnnotation(new IndexAttribute("UX_ClinicalVariable_Code") { IsUnique = true }));

            // SnomedCode: único sólo entre las variables activas y publicadas. Una variable que
            // reemplaza a otra puede llevar el mismo concepto si la anterior se da de baja. El
            // índice es funcional y se crea con SQL en la migración AddVariableReplacement:
            // EF6 no sabe declararlo.
            Property(x => x.SnomedCode).HasMaxLength(64);

            Property(x => x.Name).IsRequired().HasMaxLength(200);
            Property(x => x.Unit).HasMaxLength(32);
            Property(x => x.Accept).HasMaxLength(200);
            Property(x => x.SnomedSystem).HasMaxLength(200);
            Property(x => x.CreatedBy).HasMaxLength(128);
            Property(x => x.UpdatedBy).HasMaxLength(128);

            // Sin esto EF6 sobre Oracle usa NUMBER(18,2) y una variable con 3 decimales
            // (p. ej. peso neonatal en kg) se truncaría en silencio al guardar el rango.
            Property(x => x.MinValue).HasPrecision(18, 4);
            Property(x => x.MaxValue).HasPrecision(18, 4);

            Property(x => x.Revision).IsConcurrencyToken();
        }
    }
}
