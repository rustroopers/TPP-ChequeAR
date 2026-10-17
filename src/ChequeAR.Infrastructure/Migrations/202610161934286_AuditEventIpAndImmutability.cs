namespace ChequeAR.Infrastructure.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    /// <summary>
    /// Vuelve la IP de quien origina cada evento (la pide la spec 005) y un trigger impide
    /// modificar o borrar la auditoría con el usuario de la app.
    /// </summary>
    public partial class AuditEventIpAndImmutability : DbMigration
    {
        public override void Up()
        {
            AddColumn("CHEQUEAR.AuditEvent", "AgentIp", c => c.String(maxLength: 45, unicode: false));
            Sql(@"CREATE OR REPLACE TRIGGER ""CHEQUEAR"".""TRG_AuditEvent_Inmutable""
BEFORE UPDATE OR DELETE ON ""CHEQUEAR"".""AuditEvent""
BEGIN
  RAISE_APPLICATION_ERROR(-20010, 'La auditoría no se puede modificar ni borrar.');
END;");
        }
        
        public override void Down()
        {
            Sql(@"DROP TRIGGER ""CHEQUEAR"".""TRG_AuditEvent_Inmutable""");
            DropColumn("CHEQUEAR.AuditEvent", "AgentIp");
        }
    }
}
