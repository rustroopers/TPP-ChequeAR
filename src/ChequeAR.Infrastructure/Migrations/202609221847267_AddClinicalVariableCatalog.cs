namespace ChequeAR.Infrastructure.Migrations
{
    using System;
    using System.Configuration;
    using System.Data.Entity.Migrations;

    /// <summary>
    /// Catálogo de variables clínicas: a partir de acá un cuestionario no se escribe, se arma
    /// eligiendo variables que el Administrador cargó una sola vez, con su tipo, unidad, rango,
    /// opciones y concepto SNOMED.
    ///
    /// ATENCIÓN — esta migración BORRA todos los cuestionarios, versiones, respuestas, adjuntos
    /// y reportes guardados. El modelo nuevo declara que toda pregunta proviene de una variable,
    /// y no hay forma de inferir a qué variable correspondía una pregunta escrita a mano: dejar
    /// las viejas vivas sería dejar la base en el estado que la feature declara imposible.
    ///
    /// El borrado va acá y no como script suelto en infra/ porque MigrateDatabaseToLatestVersion
    /// corre solo al primer uso del contexto: un script aparte se olvida, y esto es precondición
    /// del cambio de esquema, no una tarea de mantenimiento aparte.
    ///
    /// Down() revierte el esquema pero NO restaura los datos borrados.
    /// </summary>
    public partial class AddClinicalVariableCatalog : DbMigration
    {
        public override void Up()
        {
            // Una base restaurada de un backup viejo volvería a pasar por acá: con fichas cargadas,
            // la purga exige Migrations:AllowDataPurge=true.
            if (!string.Equals(ConfigurationManager.AppSettings["Migrations:AllowDataPurge"], "true", StringComparison.OrdinalIgnoreCase))
                Sql(@"DECLARE n NUMBER;
BEGIN
  SELECT (SELECT COUNT(*) FROM CHEQUEAR.""Questionnaire"") + (SELECT COUNT(*) FROM CHEQUEAR.""QuestionnaireResponse"") INTO n FROM DUAL;
  IF n > 0 THEN
    RAISE_APPLICATION_ERROR(-20001, 'AddClinicalVariableCatalog borra cuestionarios y fichas. Para aplicarla, Migrations:AllowDataPurge=true.');
  END IF;
END;");

            // ---- 1. Purga. Orden hijo -> padre: todas las FK del esquema están declaradas sin
            // cascade (WillCascadeOnDelete(false)), así que nada se borra solo.
            Sql(@"DELETE FROM CHEQUEAR.""QuestionnaireResponseItem""");
            Sql(@"DELETE FROM CHEQUEAR.""QuestionnaireResponse""");
            Sql(@"DELETE FROM CHEQUEAR.""AnswerOption""");
            Sql(@"DELETE FROM CHEQUEAR.""Question""");
            Sql(@"DELETE FROM CHEQUEAR.""QuestionGroup""");
            Sql(@"DELETE FROM CHEQUEAR.""QuestionnaireVersion""");
            Sql(@"DELETE FROM CHEQUEAR.""Questionnaire""");
            // Adjuntos: el único que los referenciaba era QuestionnaireResponseItem.AttachmentId,
            // así que sin respuestas quedan archivos huérfanos.
            Sql(@"DELETE FROM CHEQUEAR.""Attachment""");
            // Reportes: SavedReport.QueryJson y ReportExecution.QueryJson guardan ids de
            // cuestionario DENTRO del JSON, sin FK que los proteja. Si sobreviven, quedan
            // reportes apuntando a cuestionarios que ya no existen y que fallan al ejecutarse.
            Sql(@"DELETE FROM CHEQUEAR.""ExportJob""");
            Sql(@"DELETE FROM CHEQUEAR.""ReportExecution""");
            Sql(@"DELETE FROM CHEQUEAR.""ExportArtifact""");
            Sql(@"DELETE FROM CHEQUEAR.""SavedReport""");

            // ---- 2. Esquema nuevo.
            CreateTable(
                "CHEQUEAR.ClinicalVariable",
                c => new
                    {
                        Id = c.Guid(nullable: false),
                        Code = c.String(nullable: false, maxLength: 64, unicode: false),
                        Name = c.String(nullable: false, maxLength: 200, unicode: false),
                        DefaultText = c.String(maxLength: 2000, unicode: false),
                        Description = c.String(maxLength: 2000, unicode: false),
                        Type = c.Decimal(nullable: false, precision: 10, scale: 0),
                        RepeatsAllowed = c.Decimal(nullable: false, precision: 1, scale: 0),
                        Unit = c.String(maxLength: 32, unicode: false),
                        MinValue = c.Decimal(precision: 18, scale: 4),
                        MaxValue = c.Decimal(precision: 18, scale: 4),
                        MaxLength = c.Decimal(precision: 10, scale: 0),
                        Pattern = c.String(maxLength: 2000, unicode: false),
                        Accept = c.String(maxLength: 200, unicode: false),
                        SnomedSystem = c.String(maxLength: 200, unicode: false),
                        SnomedCode = c.String(maxLength: 64, unicode: false),
                        SnomedDisplay = c.String(maxLength: 2000, unicode: false),
                        Term = c.String(maxLength: 2000, unicode: false),
                        IsActive = c.Decimal(nullable: false, precision: 1, scale: 0),
                        SortOrder = c.Decimal(nullable: false, precision: 10, scale: 0),
                        Revision = c.Decimal(nullable: false, precision: 10, scale: 0),
                        CreatedBy = c.String(maxLength: 128, unicode: false),
                        CreatedAt = c.DateTime(nullable: false),
                        UpdatedBy = c.String(maxLength: 128, unicode: false),
                        UpdatedAt = c.DateTime(nullable: false),
                    })
                .PrimaryKey(t => t.Id)
                // Únicos aun sobre variables dadas de baja: reactivar "peso" no debe chocar con
                // un "peso" nuevo creado mientras tanto. SnomedCode es nullable y en Oracle un
                // índice único admite múltiples NULL, así que "a lo sumo una variable por
                // concepto, y las no codificadas conviven" sale sin índice filtrado.
                .Index(t => t.Code, unique: true, name: "UX_ClinicalVariable_Code")
                .Index(t => t.SnomedCode, unique: true, name: "UX_ClinicalVariable_Snomed");

            CreateTable(
                "CHEQUEAR.VariableOption",
                c => new
                    {
                        Id = c.Guid(nullable: false),
                        VariableId = c.Guid(nullable: false),
                        Value = c.String(maxLength: 2000, unicode: false),
                        Display = c.String(maxLength: 2000, unicode: false),
                        Code = c.String(maxLength: 2000, unicode: false),
                        SnomedSystem = c.String(maxLength: 2000, unicode: false),
                        SnomedCode = c.String(maxLength: 2000, unicode: false),
                        SnomedDisplay = c.String(maxLength: 2000, unicode: false),
                        OrderIndex = c.Decimal(nullable: false, precision: 10, scale: 0),
                    })
                .PrimaryKey(t => t.Id)
                .ForeignKey("CHEQUEAR.ClinicalVariable", t => t.VariableId)
                .Index(t => t.VariableId);

            // AnswerOption.SnomedCode / SnomedDisplay NO se agregan acá: ya existen en la base
            // desde 202607291803214_Q_AnswerOptionSnomedCoding, que las creó sin entidad que las
            // mapeara. El scaffolder las vuelve a proponer porque el snapshot de aquella
            // migración no las tenía; agregarlas de nuevo sería ORA-01430 (columna duplicada).
            // Lo único que cambió ahora es que el POCO AnswerOption por fin las declara.

            // VariableId queda nullable en la base a propósito: un Questionnaire FHIR posteado
            // por un sistema externo llega sin variable, y con NOT NULL eso sería un ORA-01400
            // (500) en vez del 422 que corresponde. La regla dura ("toda pregunta viene de una
            // variable activa") la aplica QuestionnaireService.
            AddColumn("CHEQUEAR.Question", "VariableId", c => c.Guid());
            // NOT NULL sin default sobre una tabla con filas lo rechaza Oracle; funciona porque
            // la purga de arriba dejó Question vacía.
            AddColumn("CHEQUEAR.Question", "VariableRevision", c => c.Decimal(nullable: false, precision: 10, scale: 0));
        }

        public override void Down()
        {
            DropForeignKey("CHEQUEAR.VariableOption", "VariableId", "CHEQUEAR.ClinicalVariable");
            DropIndex("CHEQUEAR.VariableOption", new[] { "VariableId" });
            DropIndex("CHEQUEAR.ClinicalVariable", "UX_ClinicalVariable_Snomed");
            DropIndex("CHEQUEAR.ClinicalVariable", "UX_ClinicalVariable_Code");
            DropColumn("CHEQUEAR.Question", "VariableRevision");
            DropColumn("CHEQUEAR.Question", "VariableId");
            DropTable("CHEQUEAR.VariableOption");
            DropTable("CHEQUEAR.ClinicalVariable");
        }
    }
}
