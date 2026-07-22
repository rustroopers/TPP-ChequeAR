namespace ChequeAR.Infrastructure.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    /// <summary>
    /// Concurrencia optimista sobre Questionnaire (ConcurrencyVersion, que viaja como ETag
    /// e If-Match) y unicidad de (QuestionnaireId, VersionNumber) para que dos
    /// publicaciones simultáneas no puedan crear dos veces la misma versión.
    /// </summary>
    public partial class AddOptimisticConcurrency : DbMigration
    {
        public override void Up()
        {
            // Igual que en la migración anterior: nullable -> backfill -> NOT NULL, porque
            // Oracle rechaza una columna obligatoria sin DEFAULT sobre una tabla con filas
            // (ORA-01758) y el provider no emite DEFAULT a partir de defaultValue.
            AddColumn("CHEQUEAR.Questionnaire", "ConcurrencyVersion", c => c.Decimal(nullable: true, precision: 10, scale: 0));
            Sql(@"UPDATE ""CHEQUEAR"".""Questionnaire"" SET ""ConcurrencyVersion"" = 0 WHERE ""ConcurrencyVersion"" IS NULL");
            AlterColumn("CHEQUEAR.Questionnaire", "ConcurrencyVersion", c => c.Decimal(nullable: false, precision: 10, scale: 0));

            // El índice compuesto arranca por QuestionnaireId, así que cubre las mismas
            // búsquedas que el simple que reemplaza.
            DropIndex("CHEQUEAR.QuestionnaireVersion", new[] { "QuestionnaireId" });
            CreateIndex("CHEQUEAR.QuestionnaireVersion", new[] { "QuestionnaireId", "VersionNumber" }, unique: true, name: "UX_QuestionnaireVersion_Number");
        }
        
        public override void Down()
        {
            DropIndex("CHEQUEAR.QuestionnaireVersion", "UX_QuestionnaireVersion_Number");
            DropColumn("CHEQUEAR.Questionnaire", "ConcurrencyVersion");
            CreateIndex("CHEQUEAR.QuestionnaireVersion", "QuestionnaireId");
        }
    }
}
