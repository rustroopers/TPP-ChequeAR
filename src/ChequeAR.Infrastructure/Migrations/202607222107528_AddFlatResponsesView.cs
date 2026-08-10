namespace ChequeAR.Infrastructure.Migrations
{
    using System;
    using System.Data.Entity.Migrations;

    /// <summary>
    /// Vista analítica que consume el módulo de reportes (QueryBuilder). Estaba
    /// especificada en specs/004-analisis-exportacion/data-model.md pero la migración que
    /// la crea nunca se escribió (T002), así que toda consulta de reportes moría con
    /// ORA-00942: la vista no existía en la base.
    ///
    /// Formato "largo": una fila por respuesta-pregunta. ReportPivot la pasa a "ancho".
    /// </summary>
    public partial class AddFlatResponsesView : DbMigration
    {
        public override void Up()
        {
            // ValueText: representación legible del valor, sin importar en qué columna
            // tipada haya caído. Es la que usa el pivot para armar cada celda.
            //
            // LEFT JOIN a QuestionnaireVersion y no INNER como decía la spec: las fichas
            // anteriores al anclaje de versión guardaron QuestionnaireVersionId vacío, y
            // con un INNER desaparecerían de todos los reportes. Es preferible reportarlas
            // sin número de versión que perderlas.
            Sql(@"CREATE OR REPLACE VIEW ""CHEQUEAR"".""vw_FlatResponses"" AS
SELECT
  qr.""Id""                AS ""ResponseId"",
  qr.""PatientHospitalId"" AS ""PatientHospitalId"",
  qr.""AuthoredAt""        AS ""AuthoredAt"",
  qr.""QuestionnaireId""   AS ""QuestionnaireId"",
  qrv.""VersionNumber""    AS ""QuestionnaireVersion"",
  qri.""LinkId""           AS ""LinkId"",
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
WHERE qr.""Status"" = 1");

            // Acelera el filtro por cuestionario, que es el WHERE fijo de todo reporte.
            // -955 = el índice ya existe.
            Sql(@"BEGIN
  EXECUTE IMMEDIATE 'CREATE INDEX ""CHEQUEAR"".""IX_Response_Questionnaire"" ON ""CHEQUEAR"".""QuestionnaireResponse"" (""QuestionnaireId"", ""Status"")';
EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF;
END;");
        }

        public override void Down()
        {
            // -1418 = el índice no existe; -942 = la vista no existe.
            Sql(@"BEGIN
  EXECUTE IMMEDIATE 'DROP INDEX ""CHEQUEAR"".""IX_Response_Questionnaire""';
EXCEPTION WHEN OTHERS THEN IF SQLCODE != -1418 THEN RAISE; END IF;
END;");
            Sql(@"BEGIN
  EXECUTE IMMEDIATE 'DROP VIEW ""CHEQUEAR"".""vw_FlatResponses""';
EXCEPTION WHEN OTHERS THEN IF SQLCODE != -942 THEN RAISE; END IF;
END;");
        }
    }
}
