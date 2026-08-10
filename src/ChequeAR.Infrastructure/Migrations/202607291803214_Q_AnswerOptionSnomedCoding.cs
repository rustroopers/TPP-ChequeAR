namespace ChequeAR.Infrastructure.Migrations
{
    using System;
    using System.Data.Entity.Migrations;

    /// <summary>
    /// Codificación SNOMED CT por opción de respuesta. En una pregunta de opción única o
    /// múltiple el significado clínico está en la respuesta ("dolor punzante" vs "dolor
    /// opresivo"), así que codificar sólo el enunciado dejaba las opciones como etiquetas
    /// sueltas, no comparables fuera de ChequeAR.
    ///
    /// Columnas nuevas y nullables: se suman a Value/Display/Code en vez de reemplazarlos.
    /// El texto de la opción sigue siendo su identidad —es lo que las respuestas ya
    /// guardadas tienen en ValueCode y por lo que agrupan los reportes—, así que las filas
    /// existentes quedan válidas y sin codificar hasta que alguien las codifique.
    /// </summary>
    public partial class Q_AnswerOptionSnomedCoding : DbMigration
    {
        public override void Up()
        {
            AddColumn("CHEQUEAR.AnswerOption", "SnomedCode", c => c.String(maxLength: 2000, unicode: false));
            AddColumn("CHEQUEAR.AnswerOption", "SnomedDisplay", c => c.String(maxLength: 2000, unicode: false));
        }

        public override void Down()
        {
            DropColumn("CHEQUEAR.AnswerOption", "SnomedDisplay");
            DropColumn("CHEQUEAR.AnswerOption", "SnomedCode");
        }
    }
}
