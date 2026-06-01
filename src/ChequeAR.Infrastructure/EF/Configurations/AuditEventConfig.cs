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
            Property(x => x.AgentUserId).HasMaxLength(128);
            Property(x => x.EntityRef).HasMaxLength(255);
            Property(x => x.EntityType).HasMaxLength(64);
            Property(x => x.Category).HasMaxLength(32);
            Property(x => x.Action).HasMaxLength(1);
            Property(x => x.Outcome).HasMaxLength(8);
        }
    }
}
