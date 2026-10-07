namespace ChequeAR.Infrastructure.Migrations
{
    using System.Data.Entity.Migrations;

    /// <summary>
    /// La vista analítica sólo proyectaba fichas Completed (Status = 1). Una ficha Amended
    /// es una ficha finalizada y luego corregida: sigue siendo un dato válido, y dejarla
    /// afuera la hacía desaparecer de reportes, estadísticas, exportaciones y evolución
    /// sin ningún aviso. InProgress y EnteredInError siguen excluidas.
    ///
    /// No cambia el modelo: sólo se rehace la vista.
    /// </summary>
    public partial class IncludeAmendedInFlatResponsesView : DbMigration
    {
        public override void Up()
        {
            Sql(View(includeAmended: true));
        }

        public override void Down()
        {
            Sql(View(includeAmended: false));
        }

        private static string View(bool includeAmended)
        {
            var status = includeAmended ? @"qr.""Status"" IN (1, 2)" : @"qr.""Status"" = 1";
            return @"CREATE OR REPLACE VIEW ""CHEQUEAR"".""vw_FlatResponses"" AS
SELECT
  qr.""Id""                AS ""ResponseId"",
  qr.""PatientHospitalId"" AS ""PatientHospitalId"",
  qr.""AuthoredAt""        AS ""AuthoredAt"",
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
    TO_CHAR(qri.""ValueDecimal""),
    TO_CHAR(qri.""ValueDate"", 'YYYY-MM-DD'),
    CASE qri.""ValueBoolean"" WHEN 1 THEN 'Si' WHEN 0 THEN 'No' END
  )                        AS ""ValueText""
FROM ""CHEQUEAR"".""QuestionnaireResponse"" qr
LEFT JOIN ""CHEQUEAR"".""QuestionnaireVersion"" qrv
       ON qrv.""Id"" = qr.""QuestionnaireVersionId""
JOIN ""CHEQUEAR"".""QuestionnaireResponseItem"" qri
       ON qri.""ResponseId"" = qr.""Id""
WHERE " + status;
        }
    }
}
