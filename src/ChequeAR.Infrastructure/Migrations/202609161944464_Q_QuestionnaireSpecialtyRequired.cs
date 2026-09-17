namespace ChequeAR.Infrastructure.Migrations
{
    using System;
    using System.Data.Entity.Migrations;

    public partial class Q_QuestionnaireSpecialtyRequired : DbMigration
    {
        public override void Up()
        {
            // 1. Backfill. En Oracle '' es NULL, un solo IS NULL alcanza.
            Sql(@"UPDATE CHEQUEAR.""Questionnaire"" SET ""SpecialtyCode"" = 'general' WHERE ""SpecialtyCode"" IS NULL");
            // 2. Huérfanos: texto libre de antes del catálogo, o un import FHIR con otro
            //    vocabulario. Sin esto el ALTER TABLE ... ADD CONSTRAINT de más abajo falla
            //    con ORA-02298 (violación de integridad referencial) y deja la migración a
            //    medio aplicar.
            Sql(@"UPDATE CHEQUEAR.""Questionnaire"" SET ""SpecialtyCode"" = 'general'
                  WHERE ""SpecialtyCode"" NOT IN (SELECT ""Code"" FROM CHEQUEAR.""Specialty"")");

            // 3. NOT NULL (patrón de 202607221724132_AddOptimisticConcurrency: backfill antes
            //    del AlterColumn, Oracle rechaza NOT NULL sin default sobre tabla con filas).
            AlterColumn("CHEQUEAR.Questionnaire", "SpecialtyCode", c => c.String(nullable: false, maxLength: 64, unicode: false));

            // 4. Consistencia: SavedReport.SpecialtyCode estaba en maxLength 2000 (por la
            //    convención global de strings) contra 64 del resto. Nullable: Scope="private"
            //    no tiene especialidad.
            AlterColumn("CHEQUEAR.SavedReport", "SpecialtyCode", c => c.String(maxLength: 64, unicode: false));

            // 5. FK + índices. Sin precedente de AddForeignKey incremental en este repo (las
            //    FK existentes se declaran inline en CreateTable dentro de InitialCreate): se
            //    usa Sql crudo, con los identificadores entre comillas dobles como en el resto
            //    de las migraciones de este proyecto (ver AddFlatResponsesView).
            Sql(@"ALTER TABLE CHEQUEAR.""Questionnaire"" ADD CONSTRAINT ""FK_Questionnaire_Specialty""
                  FOREIGN KEY (""SpecialtyCode"") REFERENCES CHEQUEAR.""Specialty""(""Code"")");
            Sql(@"CREATE INDEX ""IX_Questionnaire_SpecialtyCode"" ON CHEQUEAR.""Questionnaire""(""SpecialtyCode"")");
            Sql(@"CREATE INDEX ""IX_QuestionnaireResponse_Questionnaire"" ON CHEQUEAR.""QuestionnaireResponse""(""QuestionnaireId"")");
        }

        public override void Down()
        {
            Sql(@"DROP INDEX CHEQUEAR.""IX_QuestionnaireResponse_Questionnaire""");
            Sql(@"DROP INDEX CHEQUEAR.""IX_Questionnaire_SpecialtyCode""");
            Sql(@"ALTER TABLE CHEQUEAR.""Questionnaire"" DROP CONSTRAINT ""FK_Questionnaire_Specialty""");
            AlterColumn("CHEQUEAR.SavedReport", "SpecialtyCode", c => c.String(maxLength: 2000, unicode: false));
            AlterColumn("CHEQUEAR.Questionnaire", "SpecialtyCode", c => c.String(maxLength: 64, unicode: false));
        }
    }
}
