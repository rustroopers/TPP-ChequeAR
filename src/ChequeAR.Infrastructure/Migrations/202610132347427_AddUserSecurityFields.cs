namespace ChequeAR.Infrastructure.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    /// <summary>
    /// FailedLoginCount va por SQL: el provider de Oracle ignora defaultValue y sin DEFAULT da
    /// ORA-01758. Va primero porque cada DDL hace commit y la migración quedaría a medias.
    /// </summary>
    public partial class AddUserSecurityFields : DbMigration
    {
        public override void Up()
        {
            Sql(@"alter table ""CHEQUEAR"".""AppUser"" add (""FailedLoginCount"" number(10, 0) default 0 not null)");
            AddColumn("CHEQUEAR.AppUser", "SecurityStamp", c => c.String(maxLength: 64, unicode: false));
            AddColumn("CHEQUEAR.AppUser", "LockoutUntil", c => c.DateTime());
        }
        
        public override void Down()
        {
            DropColumn("CHEQUEAR.AppUser", "LockoutUntil");
            DropColumn("CHEQUEAR.AppUser", "FailedLoginCount");
            DropColumn("CHEQUEAR.AppUser", "SecurityStamp");
        }
    }
}
