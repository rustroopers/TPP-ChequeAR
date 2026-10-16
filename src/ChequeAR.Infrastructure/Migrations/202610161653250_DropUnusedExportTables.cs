namespace ChequeAR.Infrastructure.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    /// <summary>
    /// Tablas de la exportación asincrónica y la marca de pre-carga, que quedaron fuera de
    /// alcance y nunca se escribieron.
    /// </summary>
    public partial class DropUnusedExportTables : DbMigration
    {
        public override void Up()
        {
            DropColumn("CHEQUEAR.QuestionnaireResponseItem", "WasPrefilled");
            DropTable("CHEQUEAR.ExportArtifact");
            DropTable("CHEQUEAR.ExportJob");
            DropTable("CHEQUEAR.ReportExecution");
        }
        
        public override void Down()
        {
            CreateTable(
                "CHEQUEAR.ReportExecution",
                c => new
                    {
                        Id = c.Guid(nullable: false),
                        SavedReportId = c.Guid(),
                        QueryJson = c.String(unicode: false),
                        ExecutedBy = c.String(maxLength: 2000, unicode: false),
                        ExecutedAt = c.DateTime(nullable: false),
                        RowCount = c.Decimal(nullable: false, precision: 10, scale: 0),
                        DurationMs = c.Decimal(nullable: false, precision: 10, scale: 0),
                        ArtifactId = c.Guid(),
                        Status = c.String(maxLength: 2000, unicode: false),
                    })
                .PrimaryKey(t => t.Id);
            
            CreateTable(
                "CHEQUEAR.ExportJob",
                c => new
                    {
                        Id = c.Guid(nullable: false),
                        QueryJson = c.String(unicode: false),
                        Format = c.String(maxLength: 2000, unicode: false),
                        RequestedBy = c.String(maxLength: 2000, unicode: false),
                        Status = c.String(maxLength: 2000, unicode: false),
                        Progress = c.Decimal(nullable: false, precision: 18, scale: 2),
                        ArtifactId = c.Guid(),
                        Error = c.String(maxLength: 2000, unicode: false),
                        CreatedAt = c.DateTime(nullable: false),
                        CompletedAt = c.DateTime(),
                    })
                .PrimaryKey(t => t.Id);
            
            CreateTable(
                "CHEQUEAR.ExportArtifact",
                c => new
                    {
                        Id = c.Guid(nullable: false),
                        Format = c.String(maxLength: 2000, unicode: false),
                        StoragePath = c.String(maxLength: 2000, unicode: false),
                        SizeBytes = c.Decimal(nullable: false, precision: 19, scale: 0),
                        Sha256 = c.String(maxLength: 2000, unicode: false),
                        CreatedBy = c.String(maxLength: 2000, unicode: false),
                        CreatedAt = c.DateTime(nullable: false),
                        ExpiresAt = c.DateTime(nullable: false),
                    })
                .PrimaryKey(t => t.Id);
            
            // SQL: el proveedor de Oracle no emite el DEFAULT y con filas el NOT NULL falla.
            Sql(@"ALTER TABLE ""CHEQUEAR"".""QuestionnaireResponseItem"" ADD (""WasPrefilled"" NUMBER(1) DEFAULT 0 NOT NULL)");
        }
    }
}
