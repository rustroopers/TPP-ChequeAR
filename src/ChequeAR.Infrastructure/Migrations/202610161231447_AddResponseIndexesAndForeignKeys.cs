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
            CreateIndex("CHEQUEAR.QuestionnaireResponse", new[] { "PatientHospitalId", "AuthoredAt" }, name: "IX_Response_Patient");
            CreateIndex("CHEQUEAR.RefreshToken", "TokenHash", unique: true, name: "UX_RefreshToken_Hash");
            Sql(@"ALTER TABLE ""CHEQUEAR"".""QuestionnaireResponse"" ADD CONSTRAINT ""FK_Response_Questionnaire""
  FOREIGN KEY (""QuestionnaireId"") REFERENCES ""CHEQUEAR"".""Questionnaire"" (""Id"") ENABLE NOVALIDATE");
            Sql(@"ALTER TABLE ""CHEQUEAR"".""QuestionnaireResponse"" ADD CONSTRAINT ""FK_Response_Version""
  FOREIGN KEY (""QuestionnaireVersionId"") REFERENCES ""CHEQUEAR"".""QuestionnaireVersion"" (""Id"") ENABLE NOVALIDATE");
        }
        
        public override void Down()
        {
            Sql(@"ALTER TABLE ""CHEQUEAR"".""QuestionnaireResponse"" DROP CONSTRAINT ""FK_Response_Version""");
            Sql(@"ALTER TABLE ""CHEQUEAR"".""QuestionnaireResponse"" DROP CONSTRAINT ""FK_Response_Questionnaire""");
            DropIndex("CHEQUEAR.RefreshToken", "UX_RefreshToken_Hash");
            DropIndex("CHEQUEAR.QuestionnaireResponse", "IX_Response_Patient");
            AlterColumn("CHEQUEAR.RefreshToken", "TokenHash", c => c.String(maxLength: 2000, unicode: false));
        }
    }
}
