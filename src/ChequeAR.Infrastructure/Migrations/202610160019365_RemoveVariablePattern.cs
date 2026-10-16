namespace ChequeAR.Infrastructure.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class RemoveVariablePattern : DbMigration
    {
        public override void Up()
        {
            DropColumn("CHEQUEAR.ClinicalVariable", "Pattern");
        }
        
        public override void Down()
        {
            AddColumn("CHEQUEAR.ClinicalVariable", "Pattern", c => c.String(maxLength: 2000, unicode: false));
        }
    }
}
