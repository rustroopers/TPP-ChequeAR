namespace ChequeAR.Infrastructure.Migrations
{
    using System;
    using System.Data.Entity.Migrations;

    /// <summary>
    /// Concurrencia optimista sobre QuestionnaireResponse (mismo mecanismo que
    /// AddOptimisticConcurrency le dio a Questionnaire) más el lock pesimista de sesión
    /// de edición (LockToken/LockedByUserId/LockedAt/LockExpiresAt), para que dos médicos
    /// no puedan completar el mismo cuestionario a la vez durante un seguimiento asincrónico
    /// de varias semanas.
    /// </summary>
    public partial class AddQuestionnaireResponseLocking : DbMigration
    {
        public override void Up()
        {
            // nullable -> backfill -> NOT NULL: Oracle rechaza NOT NULL sin DEFAULT sobre
            // una tabla con filas (ORA-01758) y el provider no emite DEFAULT a partir de
            // defaultValue. Igual criterio que AddOptimisticConcurrency para Questionnaire.
            AddColumn("CHEQUEAR.QuestionnaireResponse", "ConcurrencyVersion", c => c.Decimal(nullable: true, precision: 10, scale: 0));
            Sql(@"UPDATE ""CHEQUEAR"".""QuestionnaireResponse"" SET ""ConcurrencyVersion"" = 0 WHERE ""ConcurrencyVersion"" IS NULL");
            AlterColumn("CHEQUEAR.QuestionnaireResponse", "ConcurrencyVersion", c => c.Decimal(nullable: false, precision: 10, scale: 0));

            // Lock de sesión: las 4 columnas quedan nullable, es estado transitorio (nadie
            // las tiene tomadas la mayor parte del tiempo). precision:6 = TIMESTAMP en vez
            // de DATE, para no truncar a segundos al comparar contra "ahora" y decidir si
            // el lock venció (mismo motivo que Q_AuditRecordedTimestamp).
            AddColumn("CHEQUEAR.QuestionnaireResponse", "LockToken", c => c.Guid());
            AddColumn("CHEQUEAR.QuestionnaireResponse", "LockedByUserId", c => c.String(maxLength: 128, unicode: false));
            AddColumn("CHEQUEAR.QuestionnaireResponse", "LockedAt", c => c.DateTime(precision: 6));
            AddColumn("CHEQUEAR.QuestionnaireResponse", "LockExpiresAt", c => c.DateTime(precision: 6));
        }

        public override void Down()
        {
            DropColumn("CHEQUEAR.QuestionnaireResponse", "LockExpiresAt");
            DropColumn("CHEQUEAR.QuestionnaireResponse", "LockedAt");
            DropColumn("CHEQUEAR.QuestionnaireResponse", "LockedByUserId");
            DropColumn("CHEQUEAR.QuestionnaireResponse", "LockToken");
            DropColumn("CHEQUEAR.QuestionnaireResponse", "ConcurrencyVersion");
        }
    }
}
