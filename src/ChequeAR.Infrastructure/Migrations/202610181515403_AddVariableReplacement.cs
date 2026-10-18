namespace ChequeAR.Infrastructure.Migrations
{
    using System;
    using System.Data.Entity.Migrations;

    /// <summary>
    /// Un cambio clínico en una variable publicada crea otra variable que la reemplaza
    /// (ReplacesId). El concepto SNOMED pasa a ser único sólo entre las activas y publicadas:
    /// la nueva puede llevar el mismo concepto si la anterior se da de baja. Ese índice es
    /// funcional y EF6 no sabe declararlo, así que va por SQL, igual que la FK a la misma tabla.
    /// DeactivateReplacedOnPublish va por SQL por ORA-01758 (el provider ignora defaultValue).
    /// </summary>
    public partial class AddVariableReplacement : DbMigration
    {
        public override void Up()
        {
            Sql(@"alter table ""CHEQUEAR"".""ClinicalVariable"" add (""DeactivateReplacedOnPublish"" number(1, 0) default 0 not null)");
            AddColumn("CHEQUEAR.ClinicalVariable", "ReplacesId", c => c.Guid());
            Sql(@"alter table ""CHEQUEAR"".""ClinicalVariable"" add constraint ""FK_CV_Replaces""
foreign key (""ReplacesId"") references ""CHEQUEAR"".""ClinicalVariable"" (""Id"")");
            Sql(@"create index ""CHEQUEAR"".""IX_CV_Replaces"" on ""CHEQUEAR"".""ClinicalVariable"" (""ReplacesId"")");

            // Se busca por columna y no por nombre: el provider ignoró el nombre pedido en
            // AddClinicalVariableCatalog y lo creó como IX_ClinicalVariable_SnomedCode, y
            // DropIndex con un nombre que no existe se traga el error y deja el índice vivo.
            Sql(@"begin
  for i in (select ic.index_name from all_ind_columns ic
              join all_indexes ix on ix.owner = ic.index_owner and ix.index_name = ic.index_name
             where ic.index_owner = 'CHEQUEAR' and ic.table_name = 'ClinicalVariable'
               and ic.column_name = 'SnomedCode' and ix.uniqueness = 'UNIQUE') loop
    execute immediate 'drop index ""CHEQUEAR"".""' || i.index_name || '""';
  end loop;
end;");
            Sql(@"create unique index ""CHEQUEAR"".""UX_CV_Snomed_Activa"" on ""CHEQUEAR"".""ClinicalVariable""
(case when ""IsActive"" = 1 and ""CurrentVersionId"" is not null then ""SnomedCode"" end)");
        }

        public override void Down()
        {
            Sql(@"drop index ""CHEQUEAR"".""UX_CV_Snomed_Activa""");
            Sql(@"create unique index ""CHEQUEAR"".""UX_ClinicalVariable_Snomed"" on ""CHEQUEAR"".""ClinicalVariable"" (""SnomedCode"")");
            Sql(@"drop index ""CHEQUEAR"".""IX_CV_Replaces""");
            Sql(@"alter table ""CHEQUEAR"".""ClinicalVariable"" drop constraint ""FK_CV_Replaces""");
            DropColumn("CHEQUEAR.ClinicalVariable", "ReplacesId");
            DropColumn("CHEQUEAR.ClinicalVariable", "DeactivateReplacedOnPublish");
        }
    }
}
