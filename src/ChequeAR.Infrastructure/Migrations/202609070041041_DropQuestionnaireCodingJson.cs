namespace ChequeAR.Infrastructure.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class DropQuestionnaireCodingJson : DbMigration
    {
        public override void Up()
        {
            DropColumn("CHEQUEAR.Questionnaire", "CodingJson");
        }
        
        public override void Down()
        {
            AddColumn("CHEQUEAR.Questionnaire", "CodingJson", c => c.String(unicode: false));
        }
    }
}
