using System.Data.Entity.ModelConfiguration;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Infrastructure.EF.Configurations
{
    public class AuditEventConfig : EntityTypeConfiguration<AuditEvent>
    {
        public AuditEventConfig()
        {
            ToTable("AuditEvent");
            HasKey(x => x.Id);
            // TIMESTAMP y no DATE (el default de DateTime en Oracle): DATE tiene resolución
            // de segundos, así que dos eventos del mismo segundo (p.ej. guardar y publicar
            // seguidos) quedaban con igual "Recorded" y el ORDER BY los devolvía en orden
            // arbitrario -> en auditoría se veía "publicado" antes que "editado". Con el
            // store type explícito el parámetro se liga como TIMESTAMP y conserva la fracción.
            Property(x => x.Recorded).HasColumnType("timestamp");
            Property(x => x.AgentUserId).HasMaxLength(128);
            Property(x => x.EntityRef).HasMaxLength(255);
            Property(x => x.EntityType).HasMaxLength(64);
            Property(x => x.Category).HasMaxLength(32);
            Property(x => x.Action).HasMaxLength(1);
            Property(x => x.Outcome).HasMaxLength(8);
        }
    }
}
