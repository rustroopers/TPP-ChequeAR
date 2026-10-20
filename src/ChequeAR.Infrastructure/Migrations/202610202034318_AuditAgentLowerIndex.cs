namespace ChequeAR.Infrastructure.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    /// <summary>
    /// Índice funcional para filtrar la auditoría por usuario: la pantalla busca con
    /// LOWER("AgentUserId") LIKE 'x%', y sin este índice Oracle recorre toda la tabla, que
    /// sólo crece. Va por SQL porque EF6 no modela índices sobre expresiones.
    /// </summary>
    public partial class AuditAgentLowerIndex : DbMigration
    {
        public override void Up()
        {
            Sql(@"create index ""CHEQUEAR"".""IX_AuditEvent_AgentLower"" on ""CHEQUEAR"".""AuditEvent"" (lower(""AgentUserId""))");
        }
        
        public override void Down()
        {
            Sql(@"drop index ""CHEQUEAR"".""IX_AuditEvent_AgentLower""");
        }
    }
}
