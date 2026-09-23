namespace ChequeAR.Infrastructure.Migrations
{
    using System;
    using System.Data.Entity.Migrations;

    public partial class AddUserPasswordHash : DbMigration
    {
        public override void Up()
        {
            // Nullable a propósito: la tabla ya tiene filas (los usuarios de demo previos),
            // y un ALTER con NOT NULL sin default fallaría contra ellas (ORA-01758). Un
            // PasswordHash null es además el estado correcto de "este usuario no puede
            // loguearse localmente" (fail-closed), no un placeholder transitorio.
            AddColumn("CHEQUEAR.AppUser", "PasswordHash", c => c.String(maxLength: 256, unicode: false));
            AddColumn("CHEQUEAR.AppUser", "PasswordUpdatedAt", c => c.DateTime());

            // El rol Observador se elimina del sistema (ver requisito: sólo quedan
            // Administrador/Jefe/Medico). No hay Down() para esto -que filas se pierden
            // acá no se pueden reconstruir- se documenta como decisión, no como bug.
            Sql(@"DELETE FROM CHEQUEAR.""UserRole"" WHERE ""Role"" = 'Observador'");

            // El modelo pasa de "N roles acumulados por usuario" a "un único rol global"
            // (ver Domain.Enums.Role/RoleHierarchy): un usuario que hoy tiene varias filas
            // (p. ej. Administrador+Jefe+Medico, como sembraba el mock anterior) se colapsa
            // al más alto. Dos pasadas, de arriba hacia abajo en la jerarquía: quien tiene
            // Administrador pierde Jefe/Medico; quien (ya sin Administrador) tiene Jefe
            // pierde Medico.
            Sql(@"DELETE FROM CHEQUEAR.""UserRole"" t
                  WHERE t.""Role"" IN ('Jefe', 'Medico')
                  AND EXISTS (
                      SELECT 1 FROM CHEQUEAR.""UserRole"" a
                      WHERE a.""UserId"" = t.""UserId"" AND a.""Role"" = 'Administrador')");
            Sql(@"DELETE FROM CHEQUEAR.""UserRole"" t
                  WHERE t.""Role"" = 'Medico'
                  AND EXISTS (
                      SELECT 1 FROM CHEQUEAR.""UserRole"" a
                      WHERE a.""UserId"" = t.""UserId"" AND a.""Role"" = 'Jefe')");

            // obs.general: usuario de demo del seed anterior (rol Observador, ya eliminado
            // arriba). AddOrUpdate nunca borra filas que dejan de estar en el seed, así que
            // sin esto quedaría huérfano -sin rol, sin poder loguearse, pero ocupando lugar
            // en /Users- en cualquier base que haya corrido el seed viejo antes de esta
            // migración. Un id de demo conocido, no dato de un usuario real.
            Sql(@"DELETE FROM CHEQUEAR.""UserSpecialty"" WHERE ""UserId"" = 'obs.general'");
            Sql(@"DELETE FROM CHEQUEAR.""AppUser"" WHERE ""Id"" = 'obs.general'");
        }

        public override void Down()
        {
            // El DELETE de Observador y el colapso de roles NO se revierten: son limpieza
            // de datos que refleja una decisión de producto (3 roles, uno por usuario), no
            // un cambio de esquema reversible.
            DropColumn("CHEQUEAR.AppUser", "PasswordUpdatedAt");
            DropColumn("CHEQUEAR.AppUser", "PasswordHash");
        }
    }
}
