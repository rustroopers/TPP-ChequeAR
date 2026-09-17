namespace ChequeAR.Infrastructure.Migrations
{
    using System;
    using System.Data.Entity.Migrations;

    public partial class AddSpecialtyCatalogColumns : DbMigration
    {
        public override void Up()
        {
            AddColumn("CHEQUEAR.Specialty", "Color", c => c.String(maxLength: 32, unicode: false));
            AddColumn("CHEQUEAR.Specialty", "SortOrder", c => c.Decimal(nullable: false, precision: 10, scale: 0, defaultValue: 0));
            AddColumn("CHEQUEAR.Specialty", "IsDefault", c => c.Decimal(nullable: false, precision: 1, scale: 0, defaultValue: 0));

            // Los 5 códigos que hoy gobiernan la app (antes hardcodeados en SpecialtyMap.cs,
            // en los <option> del editor Vue y en las clases CSS por código) pasan a vivir
            // acá. Se insertan DENTRO de esta migración (no en Configuration.Seed(), que
            // corre recién después de TODAS las migraciones) porque la migración siguiente
            // agrega la FK Questionnaire.SpecialtyCode -> Specialty.Code, y esa FK no se
            // puede crear contra un catálogo vacío (ORA-02298).
            //
            // Seed() sí se encarga, en cada arranque, de mantener Name/Color/SortOrder al
            // día (AddOrUpdate) -- esto de acá es sólo el alta inicial de las filas.
            Sql(@"INSERT INTO CHEQUEAR.""Specialty"" (""Code"", ""Name"", ""IsActive"", ""Color"", ""SortOrder"", ""IsDefault"") VALUES ('general', 'General', 1, 'green', 0, 1)");
            Sql(@"INSERT INTO CHEQUEAR.""Specialty"" (""Code"", ""Name"", ""IsActive"", ""Color"", ""SortOrder"", ""IsDefault"") VALUES ('cardio', 'Cardiología', 1, 'red', 10, 0)");
            Sql(@"INSERT INTO CHEQUEAR.""Specialty"" (""Code"", ""Name"", ""IsActive"", ""Color"", ""SortOrder"", ""IsDefault"") VALUES ('neumo', 'Neumonología', 1, 'blue', 20, 0)");
            Sql(@"INSERT INTO CHEQUEAR.""Specialty"" (""Code"", ""Name"", ""IsActive"", ""Color"", ""SortOrder"", ""IsDefault"") VALUES ('onco', 'Oncología', 1, 'violet', 30, 0)");
            // 'yellow': es la zona que faltaba en chequear-theme.css -- reuma nunca tuvo su
            // propia clase CSS y sus chips caían al gris por defecto.
            Sql(@"INSERT INTO CHEQUEAR.""Specialty"" (""Code"", ""Name"", ""IsActive"", ""Color"", ""SortOrder"", ""IsDefault"") VALUES ('reuma', 'Reumatología', 1, 'yellow', 40, 0)");
        }

        public override void Down()
        {
            Sql(@"DELETE FROM CHEQUEAR.""Specialty"" WHERE ""Code"" IN ('general','cardio','neumo','onco','reuma')");
            DropColumn("CHEQUEAR.Specialty", "IsDefault");
            DropColumn("CHEQUEAR.Specialty", "SortOrder");
            DropColumn("CHEQUEAR.Specialty", "Color");
        }
    }
}
