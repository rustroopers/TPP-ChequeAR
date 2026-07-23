namespace ChequeAR.Infrastructure.Migrations
{
    using System.Data.Entity.Migrations;

    /// <summary>
    /// Proyecta ValueInteger en la vista analítica. Estaba disponible sólo dentro del
    /// COALESCE que arma ValueText, o sea como texto: para calcular estadísticas (FR-008)
    /// sobre una pregunta de tipo entero había que re-parsear un string, y el resultado
    /// dependía del formato con el que Oracle lo hubiera convertido.
    /// </summary>
    public partial class AddValueIntegerToFlatResponsesView : DbMigration
    {
        public override void Up()
        {
            Sql(View(withValueInteger: true));
        }

        public override void Down()
        {
            Sql(View(withValueInteger: false));
        }

        private static string View(bool withValueInteger)
        {
            var valueInteger = withValueInteger ? "  qri.\"ValueInteger\"     AS \"ValueInteger\",\n" : string.Empty;
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
" + valueInteger + @"  qri.""ValueString""      AS ""ValueString"",
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
WHERE qr.""Status"" = 1";
        }
    }
}
