namespace ChequeAR.Infrastructure.Migrations
{
    using System.Data.Entity.Migrations;

    /// <summary>
    /// Suma AuthorUserId a la vista analítica. FR-004 pide poder filtrar por autor y la
    /// columna no estaba proyectada, así que el filtro no se podía escribir en SQL.
    ///
    /// Se recrea la vista completa (CREATE OR REPLACE) en vez de alterarla: Oracle no
    /// permite agregar columnas a una vista existente.
    /// </summary>
    public partial class AddAuthorToFlatResponsesView : DbMigration
    {
        public override void Up()
        {
            Sql(View(withAuthor: true));
        }

        public override void Down()
        {
            Sql(View(withAuthor: false));
        }

        private static string View(bool withAuthor)
        {
            var author = withAuthor ? "  qr.\"AuthorUserId\"      AS \"AuthorUserId\",\n" : string.Empty;
            return @"CREATE OR REPLACE VIEW ""CHEQUEAR"".""vw_FlatResponses"" AS
SELECT
  qr.""Id""                AS ""ResponseId"",
  qr.""PatientHospitalId"" AS ""PatientHospitalId"",
  qr.""AuthoredAt""        AS ""AuthoredAt"",
  qr.""QuestionnaireId""   AS ""QuestionnaireId"",
  qrv.""VersionNumber""    AS ""QuestionnaireVersion"",
" + author + @"  qri.""LinkId""           AS ""LinkId"",
  qri.""ValueDecimal""     AS ""ValueDecimal"",
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
WHERE qr.""Status"" = 1";
        }
    }
}
