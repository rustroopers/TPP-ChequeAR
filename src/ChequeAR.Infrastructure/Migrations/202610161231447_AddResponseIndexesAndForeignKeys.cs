namespace ChequeAR.Infrastructure.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    /// <summary>
    /// Índices para buscar fichas por paciente y refresh tokens por hash, y las FK de la ficha a
    /// su cuestionario y a su versión. Las FK van NOVALIDATE: rigen para lo nuevo sin revisar
    /// las fichas viejas, que pueden no tener versión registrada.
    /// </summary>
    public partial class AddResponseIndexesAndForeignKeys : DbMigration
    {
        public override void Up()
        {
            AlterColumn("CHEQUEAR.RefreshToken", "TokenHash", c => c.String(maxLength: 64, unicode: false));
            // SQL y no CreateIndex: el proveedor de Oracle ignora el nombre y genera otro, y
            // después el Down no lo encuentra.
            Sql(@"CREATE INDEX ""CHEQUEAR"".""IX_Response_Patient"" ON ""CHEQUEAR"".""QuestionnaireResponse"" (""PatientHospitalId"", ""AuthoredAt"")");
            Sql(@"CREATE UNIQUE INDEX ""CHEQUEAR"".""UX_RefreshToken_Hash"" ON ""CHEQUEAR"".""RefreshToken"" (""TokenHash"")");
            // Redundante con IX_Response_Questionnaire (QuestionnaireId, Status).
            Sql(@"DROP INDEX ""CHEQUEAR"".""IX_QuestionnaireResponse_Questionnaire""");
            Sql(@"ALTER TABLE ""CHEQUEAR"".""QuestionnaireResponse"" ADD CONSTRAINT ""FK_Response_Questionnaire""
  FOREIGN KEY (""QuestionnaireId"") REFERENCES ""CHEQUEAR"".""Questionnaire"" (""Id"") ENABLE NOVALIDATE");
            Sql(@"ALTER TABLE ""CHEQUEAR"".""QuestionnaireResponse"" ADD CONSTRAINT ""FK_Response_Version""
  FOREIGN KEY (""QuestionnaireVersionId"") REFERENCES ""CHEQUEAR"".""QuestionnaireVersion"" (""Id"") ENABLE NOVALIDATE");
        }
        
        public override void Down()
        {
            Sql(@"CREATE INDEX ""IX_QuestionnaireResponse_Questionnaire"" ON CHEQUEAR.""QuestionnaireResponse""(""QuestionnaireId"")");
            Sql(@"ALTER TABLE ""CHEQUEAR"".""QuestionnaireResponse"" DROP CONSTRAINT ""FK_Response_Version""");
            Sql(@"ALTER TABLE ""CHEQUEAR"".""QuestionnaireResponse"" DROP CONSTRAINT ""FK_Response_Questionnaire""");
            Sql(@"DROP INDEX ""CHEQUEAR"".""UX_RefreshToken_Hash""");
            Sql(@"DROP INDEX ""CHEQUEAR"".""IX_Response_Patient""");
            AlterColumn("CHEQUEAR.RefreshToken", "TokenHash", c => c.String(maxLength: 2000, unicode: false));
        }
    }
}
