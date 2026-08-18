namespace ChequeAR.Infrastructure.Migrations
{
    using System;
    using System.Data.Entity.Migrations;

    /// <summary>
    /// Codificación SNOMED CT del cuestionario completo. Es el análogo a nivel cuestionario de
    /// Question.CodingJson: el mismo contrato { system, code, display, term } pero para la ficha
    /// entera —qué mide en conjunto—, independiente del código de cada pregunta, que se conserva.
    ///
    /// Columna nueva y nullable (CLOB, como el resto de los JSON largos): las filas existentes
    /// quedan válidas y sin codificar hasta que alguien las codifique.
    /// </summary>
    public partial class QuestionnaireSnomedCoding : DbMigration
    {
        public override void Up()
        {
            AddColumn("CHEQUEAR.Questionnaire", "CodingJson", c => c.String(unicode: false));
        }
        
        public override void Down()
        {
            DropColumn("CHEQUEAR.Questionnaire", "CodingJson");
        }
    }
}
