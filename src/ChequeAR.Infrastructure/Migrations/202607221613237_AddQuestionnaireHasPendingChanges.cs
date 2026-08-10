namespace ChequeAR.Infrastructure.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    /// <summary>
    /// Marca explícita de "publicado y editado después". Reemplaza la heurística
    /// UpdatedAt &gt; PublishedAt, que no era confiable: ambas columnas son DATE en Oracle
    /// (precisión de 1 segundo), así que una edición dentro del mismo segundo que la
    /// publicación quedaba sin detectar.
    /// </summary>
    public partial class AddQuestionnaireHasPendingChanges : DbMigration
    {
        public override void Up()
        {
            // Tres pasos en vez de agregar la columna NOT NULL de una: Oracle rechaza una
            // columna obligatoria sin DEFAULT sobre una tabla con filas (ORA-01758) y el
            // provider de ODP.NET no emite DEFAULT a partir de defaultValue.
            AddColumn("CHEQUEAR.Questionnaire", "HasPendingChanges", c => c.Decimal(nullable: true, precision: 1, scale: 0));

            // Backfill: para las filas que ya existen la única información disponible es la
            // heurística vieja (se editó después de publicar). Status = 1 es Active.
            Sql(@"UPDATE ""CHEQUEAR"".""Questionnaire"" q
                     SET q.""HasPendingChanges"" =
                         CASE WHEN q.""Status"" = 1
                               AND EXISTS (SELECT 1
                                             FROM ""CHEQUEAR"".""QuestionnaireVersion"" v
                                            WHERE v.""Id"" = q.""CurrentVersionId""
                                              AND q.""UpdatedAt"" > v.""PublishedAt"")
                              THEN 1 ELSE 0 END");

            AlterColumn("CHEQUEAR.Questionnaire", "HasPendingChanges", c => c.Decimal(nullable: false, precision: 1, scale: 0));
        }

        public override void Down()
        {
            DropColumn("CHEQUEAR.Questionnaire", "HasPendingChanges");
        }
    }
}
