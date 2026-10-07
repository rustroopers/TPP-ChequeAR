namespace ChequeAR.Infrastructure.Migrations
{
    using System;
    using System.Data.Entity.Migrations;

    /// <summary>
    /// Se decidió no registrar la IP en la auditoría y la columna quedó sin uso. Bajarla
    /// descarta las IP que hubiera guardadas: Down() recrea la columna, pero vacía.
    /// </summary>
    public partial class DropAuditEventAgentIp : DbMigration
    {
        public override void Up()
        {
            DropColumn("CHEQUEAR.AuditEvent", "AgentIp");
        }
        
        public override void Down()
        {
            AddColumn("CHEQUEAR.AuditEvent", "AgentIp", c => c.String(maxLength: 2000, unicode: false));
        }
    }
}
