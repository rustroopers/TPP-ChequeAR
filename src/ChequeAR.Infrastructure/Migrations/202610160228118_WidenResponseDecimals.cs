namespace ChequeAR.Infrastructure.Migrations
{
    using System;
    using System.Data.Entity.Migrations;

    /// <summary>
    /// ValueDecimal pasa de NUMBER(18,2) a NUMBER(22,6), con la misma parte entera: Oracle
    /// redondeaba en silencio (2,345 kg quedaba en 2,35) y reportes y FHIR mostraban cifras
    /// distintas. Los valores ya redondeados se recuperan del recurso FHIR de cada ficha, que
    /// guardó el valor original.
    /// </summary>
    public partial class WidenResponseDecimals : DbMigration
    {
        public override void Up()
        {
            AlterColumn("CHEQUEAR.QuestionnaireResponseItem", "ValueDecimal", c => c.Decimal(precision: 22, scale: 6));
            Sql(@"MERGE INTO ""CHEQUEAR"".""QuestionnaireResponseItem"" i
USING (
  SELECT r.""Id"" AS rid, j.lid, MAX(j.v) AS v
  FROM ""CHEQUEAR"".""QuestionnaireResponse"" r,
       JSON_TABLE(r.""FhirJson"", '$..item[*]'
         COLUMNS (lid VARCHAR2(2000) PATH '$.linkId', v NUMBER PATH '$.answer[0].valueDecimal')) j
  WHERE j.v IS NOT NULL
  GROUP BY r.""Id"", j.lid
  HAVING COUNT(*) = 1
) s
ON (i.""ResponseId"" = s.rid AND i.""LinkId"" = s.lid)
WHEN MATCHED THEN UPDATE SET i.""ValueDecimal"" = s.v
  WHERE i.""ValueDecimal"" <> s.v AND ROUND(s.v, 2) = i.""ValueDecimal"" AND s.v = ROUND(s.v, 6)");
            Sql(AddCompletedAtToResponses.View(current: true, decimals: 6));
        }

        // La columna queda como está: Oracle no deja achicar la escala con datos (ORA-01440).
        public override void Down()
        {
            Sql(AddCompletedAtToResponses.View(current: true));
        }
    }
}
