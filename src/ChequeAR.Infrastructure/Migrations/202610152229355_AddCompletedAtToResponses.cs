namespace ChequeAR.Infrastructure.Migrations
{
    using System;
    using System.Data.Entity.Migrations;

    /// <summary>
    /// Fecha de finalización de la ficha, que es la que pide la evolución del paciente (AuthoredAt
    /// es la de creación). Las ya cerradas toman LastSavedAt, que hasta ahora se pisaba al finalizar.
    ///
    /// La vista además formatea los decimales con punto y cero inicial: sin máscara, TO_CHAR
    /// depende de la configuración regional de la sesión ("3,5" o ".5").
    /// </summary>
    public partial class AddCompletedAtToResponses : DbMigration
    {
        public override void Up()
        {
            AddColumn("CHEQUEAR.QuestionnaireResponse", "CompletedAt", c => c.DateTime());
            Sql(@"UPDATE ""CHEQUEAR"".""QuestionnaireResponse"" SET ""CompletedAt"" = ""LastSavedAt"" WHERE ""Status"" IN (1, 2)");
            Sql(View(current: true));
        }

        public override void Down()
        {
            Sql(View(current: false));
            DropColumn("CHEQUEAR.QuestionnaireResponse", "CompletedAt");
        }

        private static string View(bool current)
        {
            var completedAt = current ? @"
  qr.""CompletedAt""       AS ""CompletedAt""," : "";
            var decimalText = current
                ? @"RTRIM(TO_CHAR(qri.""ValueDecimal"", 'FM999999999999990D99', 'NLS_NUMERIC_CHARACTERS=''.,'''), '.')"
                : @"TO_CHAR(qri.""ValueDecimal"")";
            return @"CREATE OR REPLACE VIEW ""CHEQUEAR"".""vw_FlatResponses"" AS
SELECT
  qr.""Id""                AS ""ResponseId"",
  qr.""PatientHospitalId"" AS ""PatientHospitalId"",
  qr.""AuthoredAt""        AS ""AuthoredAt""," + completedAt + @"
  qr.""QuestionnaireId""   AS ""QuestionnaireId"",
  qrv.""VersionNumber""    AS ""QuestionnaireVersion"",
  qr.""AuthorUserId""      AS ""AuthorUserId"",
  qri.""LinkId""           AS ""LinkId"",
  qri.""ValueDecimal""     AS ""ValueDecimal"",
  qri.""ValueInteger""     AS ""ValueInteger"",
  qri.""ValueString""      AS ""ValueString"",
  qri.""ValueDate""        AS ""ValueDate"",
  qri.""ValueBoolean""     AS ""ValueBoolean"",
  qri.""ValueCode""        AS ""ValueCode"",
  COALESCE(
    qri.""ValueString"",
    qri.""ValueCode"",
    TO_CHAR(qri.""ValueInteger""),
    " + decimalText + @",
    TO_CHAR(qri.""ValueDate"", 'YYYY-MM-DD'),
    CASE qri.""ValueBoolean"" WHEN 1 THEN 'Si' WHEN 0 THEN 'No' END
  )                        AS ""ValueText""
FROM ""CHEQUEAR"".""QuestionnaireResponse"" qr
LEFT JOIN ""CHEQUEAR"".""QuestionnaireVersion"" qrv
       ON qrv.""Id"" = qr.""QuestionnaireVersionId""
JOIN ""CHEQUEAR"".""QuestionnaireResponseItem"" qri
       ON qri.""ResponseId"" = qr.""Id""
WHERE qr.""Status"" IN (1, 2)";
        }
    }
}
