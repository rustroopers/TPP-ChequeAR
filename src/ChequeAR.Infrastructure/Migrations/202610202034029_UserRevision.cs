namespace ChequeAR.Infrastructure.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    /// <summary>
    /// Versión de la ficha de usuario para el ABM: dos administradores editando el mismo
    /// usuario ya no se pisan en silencio (el segundo recibe un conflicto). Va por SQL: el
    /// provider de Oracle ignora defaultValue y sin DEFAULT da ORA-01758 sobre una tabla con filas.
    /// </summary>
    public partial class UserRevision : DbMigration
    {
        public override void Up()
        {
            Sql(@"alter table ""CHEQUEAR"".""AppUser"" add (""Revision"" number(10, 0) default 0 not null)");
        }
        
        public override void Down()
        {
            DropColumn("CHEQUEAR.AppUser", "Revision");
        }
    }
}
