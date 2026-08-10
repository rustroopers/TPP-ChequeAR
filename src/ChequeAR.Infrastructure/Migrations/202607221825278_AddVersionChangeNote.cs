namespace ChequeAR.Infrastructure.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddVersionChangeNote : DbMigration
    {
        public override void Up()
        {
            AddColumn("CHEQUEAR.QuestionnaireVersion", "ChangeNote", c => c.String(maxLength: 2000, unicode: false));
        }
        
        public override void Down()
        {
            DropColumn("CHEQUEAR.QuestionnaireVersion", "ChangeNote");
        }
    }
}
