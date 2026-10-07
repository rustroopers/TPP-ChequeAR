namespace ChequeAR.Infrastructure.Migrations
{
    using System.Data.Entity.Migrations;

    /// <summary>
    /// Índices del registro de auditoría. La tabla sólo tenía la clave primaria, así que el
    /// listado (ordenado por fecha, del más reciente al más viejo) y el historial de un
    /// recurso (por EntityRef) recorrían la tabla entera en cada consulta.
    ///
    /// No cambia el modelo: los índices se declaran sólo acá.
    /// </summary>
    public partial class AddAuditEventIndexes : DbMigration
    {
        public override void Up()
        {
            CreateIndex("CHEQUEAR.AuditEvent", "Recorded", name: "IX_AuditEvent_Recorded");
            CreateIndex("CHEQUEAR.AuditEvent", "EntityRef", name: "IX_AuditEvent_EntityRef");
        }

        public override void Down()
        {
            DropIndex("CHEQUEAR.AuditEvent", "IX_AuditEvent_EntityRef");
            DropIndex("CHEQUEAR.AuditEvent", "IX_AuditEvent_Recorded");
        }
    }
}
