namespace ChequeAR.Infrastructure.Migrations
{
    using System;
    using System.Data.Entity.Migrations;

    /// <summary>
    /// Una variable publicada ya no cambia su definición clínica (la reemplaza otra), así que
    /// no necesita versiones: alcanza con saber cuándo se publicó. La tabla de versiones y las
    /// columnas que apuntan a ella no se borran acá sino que quedan con nombres legacy:
    /// VariableVersionRemoval, en el Seed, las usa para poner al día variables y preguntas y
    /// después las borra.
    /// </summary>
    public partial class RemoveVariableVersions : DbMigration
    {
        public override void Up()
        {
            AddColumn("CHEQUEAR.ClinicalVariable", "PublishedAt", c => c.DateTime());
            Sql(@"update ""CHEQUEAR"".""ClinicalVariable"" v
   set ""PublishedAt"" = (select x.""PublishedAt"" from ""CHEQUEAR"".""ClinicalVariableVersion"" x where x.""Id"" = v.""CurrentVersionId"")
 where v.""CurrentVersionId"" is not null");

            Sql(@"drop index ""CHEQUEAR"".""UX_CV_Snomed_Activa""");
            Sql(@"create unique index ""CHEQUEAR"".""UX_CV_Snomed_Activa"" on ""CHEQUEAR"".""ClinicalVariable""
(case when ""IsActive"" = 1 and ""PublishedAt"" is not null then ""SnomedCode"" end)");

            Sql(@"alter table ""CHEQUEAR"".""ClinicalVariable"" rename column ""CurrentVersionId"" to ""LegacyCurrentVersionId""");
            Sql(@"alter table ""CHEQUEAR"".""Question"" rename column ""VariableVersionId"" to ""LegacyVariableVersionId""");
            DropColumn("CHEQUEAR.Question", "VariableRevision");
            Sql(@"alter table ""CHEQUEAR"".""ClinicalVariableVersion"" rename to ""CVV_Legacy""");
        }

        /// <summary>Vuelve el esquema, no los datos: las versiones ya no existen.</summary>
        public override void Down()
        {
            CreateTable(
                "CHEQUEAR.ClinicalVariableVersion",
                c => new
                    {
                        Id = c.Guid(nullable: false),
                        VariableId = c.Guid(nullable: false),
                        VersionNumber = c.Decimal(nullable: false, precision: 10, scale: 0),
                        SourceRevision = c.Decimal(nullable: false, precision: 10, scale: 0),
                        DefinitionJson = c.String(nullable: false, unicode: false),
                        PublishedBy = c.String(maxLength: 128, unicode: false),
                        PublishedAt = c.DateTime(nullable: false),
                        ChangeNote = c.String(maxLength: 2000, unicode: false),
                    })
                .PrimaryKey(t => t.Id);

            Sql(@"alter table ""CHEQUEAR"".""Question"" add (""VariableRevision"" number(10, 0) default 0 not null)");
            AddColumn("CHEQUEAR.Question", "VariableVersionId", c => c.Guid());
            AddColumn("CHEQUEAR.ClinicalVariable", "CurrentVersionId", c => c.Guid());
            Sql(@"drop index ""CHEQUEAR"".""UX_CV_Snomed_Activa""");
            Sql(@"create unique index ""CHEQUEAR"".""UX_CV_Snomed_Activa"" on ""CHEQUEAR"".""ClinicalVariable""
(case when ""IsActive"" = 1 and ""CurrentVersionId"" is not null then ""SnomedCode"" end)");
            DropColumn("CHEQUEAR.ClinicalVariable", "PublishedAt");
            CreateIndex("CHEQUEAR.ClinicalVariableVersion", new[] { "VariableId", "VersionNumber" }, unique: true, name: "UX_CVV_Variable_Number");
            AddForeignKey("CHEQUEAR.ClinicalVariableVersion", "VariableId", "CHEQUEAR.ClinicalVariable", "Id");
        }
    }
}
