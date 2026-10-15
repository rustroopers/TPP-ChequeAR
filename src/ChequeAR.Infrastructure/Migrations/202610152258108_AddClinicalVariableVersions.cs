namespace ChequeAR.Infrastructure.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    /// <summary>
    /// Versiones publicadas de las variables clínicas (spec 014). La versión 1 de las variables
    /// existentes la crea el backfill del Seed, que necesita leer cada definición.
    /// </summary>
    public partial class AddClinicalVariableVersions : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "CHEQUEAR.ClinicalVariableVersion",
                c => new
                    {
                        Id = c.Guid(nullable: false),
                        VariableId = c.Guid(nullable: false),
                        VersionNumber = c.Decimal(nullable: false, precision: 10, scale: 0),
                        SourceRevision = c.Decimal(nullable: false, precision: 10, scale: 0),
                        DefinitionJson = c.String(nullable: false, unicode: false),
                        PublishedBy = c.String(maxLength: 128, unicode: false),
                        PublishedAt = c.DateTime(nullable: false),
                        ChangeNote = c.String(maxLength: 2000, unicode: false),
                    })
                .PrimaryKey(t => t.Id)
                .ForeignKey("CHEQUEAR.ClinicalVariable", t => t.VariableId)
                .Index(t => new { t.VariableId, t.VersionNumber }, unique: true, name: "UX_CVV_Variable_Number");
            
            AddColumn("CHEQUEAR.ClinicalVariable", "CurrentVersionId", c => c.Guid());
            AddColumn("CHEQUEAR.Question", "VariableVersionId", c => c.Guid());
        }
        
        public override void Down()
        {
            DropForeignKey("CHEQUEAR.ClinicalVariableVersion", "VariableId", "CHEQUEAR.ClinicalVariable");
            DropIndex("CHEQUEAR.ClinicalVariableVersion", "UX_CVV_Variable_Number");
            DropColumn("CHEQUEAR.Question", "VariableVersionId");
            DropColumn("CHEQUEAR.ClinicalVariable", "CurrentVersionId");
            DropTable("CHEQUEAR.ClinicalVariableVersion");
        }
    }
}
