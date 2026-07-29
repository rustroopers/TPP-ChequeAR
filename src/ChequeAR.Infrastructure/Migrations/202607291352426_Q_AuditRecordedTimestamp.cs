namespace ChequeAR.Infrastructure.Migrations
{
    using System;
    using System.Data.Entity.Migrations;

    /// <summary>
    /// AuditEvent.Recorded pasa de DATE (resolución de segundos) a TIMESTAMP. Dos eventos
    /// del mismo segundo —típicamente guardar y publicar un cuestionario seguidos— quedaban
    /// con idéntico Recorded, y el ORDER BY de auditoría los devolvía en orden arbitrario:
    /// se veía "publicado" antes que "editado". Con TIMESTAMP se conserva la fracción y el
    /// orden queda determinado. El store type también importa en la escritura: con DATE el
    /// parámetro se ligaba como OracleDbType.Date y truncaba los milisegundos antes del
    /// INSERT (ver AuditEventConfig.HasColumnType("timestamp")).
    /// </summary>
    public partial class Q_AuditRecordedTimestamp : DbMigration
    {
        public override void Up()
        {
            AlterColumn("CHEQUEAR.AuditEvent", "Recorded", c => c.DateTime(nullable: false, precision: 6));
        }

        public override void Down()
        {
            AlterColumn("CHEQUEAR.AuditEvent", "Recorded", c => c.DateTime(nullable: false));
        }
    }
}
