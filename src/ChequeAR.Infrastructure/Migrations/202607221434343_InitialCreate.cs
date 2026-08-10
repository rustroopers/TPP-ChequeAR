namespace ChequeAR.Infrastructure.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class InitialCreate : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "CHEQUEAR.AnswerOption",
                c => new
                    {
                        Id = c.Guid(nullable: false),
                        QuestionId = c.Guid(nullable: false),
                        Value = c.String(maxLength: 2000, unicode: false),
                        Display = c.String(maxLength: 2000, unicode: false),
                        Code = c.String(maxLength: 2000, unicode: false),
                        OrderIndex = c.Decimal(nullable: false, precision: 10, scale: 0),
                    })
                .PrimaryKey(t => t.Id)
                .ForeignKey("CHEQUEAR.Question", t => t.QuestionId, cascadeDelete: true)
                .Index(t => t.QuestionId);
            
            CreateTable(
                "CHEQUEAR.Attachment",
                c => new
                    {
                        Id = c.Guid(nullable: false),
                        OriginalFileName = c.String(maxLength: 2000, unicode: false),
                        ContentType = c.String(maxLength: 2000, unicode: false),
                        SizeBytes = c.Decimal(nullable: false, precision: 19, scale: 0),
                        StoragePath = c.String(maxLength: 2000, unicode: false),
                        UploadedBy = c.String(maxLength: 2000, unicode: false),
                        UploadedAt = c.DateTime(nullable: false),
                        Hash = c.String(maxLength: 2000, unicode: false),
                    })
                .PrimaryKey(t => t.Id);
            
            CreateTable(
                "CHEQUEAR.AuditEvent",
                c => new
                    {
                        Id = c.Guid(nullable: false),
                        Recorded = c.DateTime(nullable: false),
                        Action = c.String(maxLength: 1, unicode: false),
                        Outcome = c.String(maxLength: 8, unicode: false),
                        AgentUserId = c.String(maxLength: 128, unicode: false),
                        AgentIp = c.String(maxLength: 2000, unicode: false),
                        EntityRef = c.String(maxLength: 255, unicode: false),
                        EntityType = c.String(maxLength: 64, unicode: false),
                        Category = c.String(maxLength: 32, unicode: false),
                        FhirJson = c.String(unicode: false),
                    })
                .PrimaryKey(t => t.Id);
            
            CreateTable(
                "CHEQUEAR.ExportArtifact",
                c => new
                    {
                        Id = c.Guid(nullable: false),
                        Format = c.String(maxLength: 2000, unicode: false),
                        StoragePath = c.String(maxLength: 2000, unicode: false),
                        SizeBytes = c.Decimal(nullable: false, precision: 19, scale: 0),
                        Sha256 = c.String(maxLength: 2000, unicode: false),
                        CreatedBy = c.String(maxLength: 2000, unicode: false),
                        CreatedAt = c.DateTime(nullable: false),
                        ExpiresAt = c.DateTime(nullable: false),
                    })
                .PrimaryKey(t => t.Id);
            
            CreateTable(
                "CHEQUEAR.ExportJob",
                c => new
                    {
                        Id = c.Guid(nullable: false),
                        QueryJson = c.String(unicode: false),
                        Format = c.String(maxLength: 2000, unicode: false),
                        RequestedBy = c.String(maxLength: 2000, unicode: false),
                        Status = c.String(maxLength: 2000, unicode: false),
                        Progress = c.Decimal(nullable: false, precision: 18, scale: 2),
                        ArtifactId = c.Guid(),
                        Error = c.String(maxLength: 2000, unicode: false),
                        CreatedAt = c.DateTime(nullable: false),
                        CompletedAt = c.DateTime(),
                    })
                .PrimaryKey(t => t.Id);
            
            CreateTable(
                "CHEQUEAR.QuestionGroup",
                c => new
                    {
                        Id = c.Guid(nullable: false),
                        QuestionnaireId = c.Guid(nullable: false),
                        ParentGroupId = c.Guid(),
                        LinkId = c.String(maxLength: 2000, unicode: false),
                        Title = c.String(maxLength: 2000, unicode: false),
                        OrderIndex = c.Decimal(nullable: false, precision: 10, scale: 0),
                    })
                .PrimaryKey(t => t.Id)
                .ForeignKey("CHEQUEAR.Questionnaire", t => t.QuestionnaireId)
                .Index(t => t.QuestionnaireId);
            
            CreateTable(
                "CHEQUEAR.Question",
                c => new
                    {
                        Id = c.Guid(nullable: false),
                        QuestionnaireId = c.Guid(nullable: false),
                        GroupId = c.Guid(),
                        LinkId = c.String(maxLength: 2000, unicode: false),
                        Text = c.String(maxLength: 2000, unicode: false),
                        Type = c.Decimal(nullable: false, precision: 10, scale: 0),
                        Required = c.Decimal(nullable: false, precision: 1, scale: 0),
                        RepeatsAllowed = c.Decimal(nullable: false, precision: 1, scale: 0),
                        OrderIndex = c.Decimal(nullable: false, precision: 10, scale: 0),
                        ValidationJson = c.String(unicode: false),
                        EnableWhenJson = c.String(unicode: false),
                        CodingJson = c.String(unicode: false),
                    })
                .PrimaryKey(t => t.Id)
                .ForeignKey("CHEQUEAR.QuestionGroup", t => t.GroupId)
                .ForeignKey("CHEQUEAR.Questionnaire", t => t.QuestionnaireId)
                .Index(t => t.QuestionnaireId)
                .Index(t => t.GroupId);
            
            CreateTable(
                "CHEQUEAR.QuestionnaireResponseItem",
                c => new
                    {
                        Id = c.Guid(nullable: false),
                        ResponseId = c.Guid(nullable: false),
                        LinkId = c.String(maxLength: 2000, unicode: false),
                        QuestionType = c.String(maxLength: 2000, unicode: false),
                        ValueString = c.String(maxLength: 2000, unicode: false),
                        ValueDecimal = c.Decimal(precision: 18, scale: 2),
                        ValueInteger = c.Decimal(precision: 10, scale: 0),
                        ValueDate = c.DateTime(),
                        ValueBoolean = c.Decimal(precision: 1, scale: 0),
                        ValueCode = c.String(maxLength: 2000, unicode: false),
                        AttachmentId = c.Guid(),
                        WasPrefilled = c.Decimal(nullable: false, precision: 1, scale: 0),
                    })
                .PrimaryKey(t => t.Id)
                .ForeignKey("CHEQUEAR.QuestionnaireResponse", t => t.ResponseId)
                .Index(t => t.ResponseId);
            
            CreateTable(
                "CHEQUEAR.QuestionnaireResponse",
                c => new
                    {
                        Id = c.Guid(nullable: false),
                        QuestionnaireId = c.Guid(nullable: false),
                        QuestionnaireVersionId = c.Guid(nullable: false),
                        PatientHospitalId = c.String(nullable: false, maxLength: 64, unicode: false),
                        EncounterId = c.String(maxLength: 2000, unicode: false),
                        Status = c.Decimal(nullable: false, precision: 10, scale: 0),
                        AuthoredAt = c.DateTime(nullable: false),
                        AuthorUserId = c.String(maxLength: 128, unicode: false),
                        FhirJson = c.String(unicode: false),
                        LastSavedAt = c.DateTime(nullable: false),
                        IsAutosave = c.Decimal(nullable: false, precision: 1, scale: 0),
                    })
                .PrimaryKey(t => t.Id);
            
            CreateTable(
                "CHEQUEAR.Questionnaire",
                c => new
                    {
                        Id = c.Guid(nullable: false),
                        Title = c.String(nullable: false, maxLength: 255, unicode: false),
                        Description = c.String(maxLength: 2000, unicode: false),
                        Status = c.Decimal(nullable: false, precision: 10, scale: 0),
                        SpecialtyCode = c.String(maxLength: 64, unicode: false),
                        SpecialtyDisplay = c.String(maxLength: 2000, unicode: false),
                        CurrentVersionId = c.Guid(),
                        CreatedBy = c.String(maxLength: 128, unicode: false),
                        CreatedAt = c.DateTime(nullable: false),
                        UpdatedAt = c.DateTime(nullable: false),
                        IsDeleted = c.Decimal(nullable: false, precision: 1, scale: 0),
                    })
                .PrimaryKey(t => t.Id);
            
            CreateTable(
                "CHEQUEAR.QuestionnaireVersion",
                c => new
                    {
                        Id = c.Guid(nullable: false),
                        QuestionnaireId = c.Guid(nullable: false),
                        VersionNumber = c.Decimal(nullable: false, precision: 10, scale: 0),
                        FhirJson = c.String(unicode: false),
                        PublishedBy = c.String(maxLength: 2000, unicode: false),
                        PublishedAt = c.DateTime(nullable: false),
                    })
                .PrimaryKey(t => t.Id)
                .ForeignKey("CHEQUEAR.Questionnaire", t => t.QuestionnaireId)
                .Index(t => t.QuestionnaireId);
            
            CreateTable(
                "CHEQUEAR.RefreshToken",
                c => new
                    {
                        Id = c.Guid(nullable: false),
                        UserId = c.String(maxLength: 128, unicode: false),
                        TokenHash = c.String(maxLength: 2000, unicode: false),
                        IssuedAt = c.DateTime(nullable: false),
                        ExpiresAt = c.DateTime(nullable: false),
                        RevokedAt = c.DateTime(),
                        UserAgent = c.String(maxLength: 2000, unicode: false),
                        Ip = c.String(maxLength: 2000, unicode: false),
                    })
                .PrimaryKey(t => t.Id);
            
            CreateTable(
                "CHEQUEAR.ReportExecution",
                c => new
                    {
                        Id = c.Guid(nullable: false),
                        SavedReportId = c.Guid(),
                        QueryJson = c.String(unicode: false),
                        ExecutedBy = c.String(maxLength: 2000, unicode: false),
                        ExecutedAt = c.DateTime(nullable: false),
                        RowCount = c.Decimal(nullable: false, precision: 10, scale: 0),
                        DurationMs = c.Decimal(nullable: false, precision: 10, scale: 0),
                        ArtifactId = c.Guid(),
                        Status = c.String(maxLength: 2000, unicode: false),
                    })
                .PrimaryKey(t => t.Id);
            
            CreateTable(
                "CHEQUEAR.SavedReport",
                c => new
                    {
                        Id = c.Guid(nullable: false),
                        Name = c.String(maxLength: 2000, unicode: false),
                        Scope = c.String(maxLength: 2000, unicode: false),
                        SpecialtyCode = c.String(maxLength: 2000, unicode: false),
                        QuestionnaireId = c.Guid(nullable: false),
                        QueryJson = c.String(unicode: false),
                        CreatedBy = c.String(maxLength: 2000, unicode: false),
                        CreatedAt = c.DateTime(nullable: false),
                        UpdatedAt = c.DateTime(nullable: false),
                    })
                .PrimaryKey(t => t.Id);
            
            CreateTable(
                "CHEQUEAR.Specialty",
                c => new
                    {
                        Code = c.String(nullable: false, maxLength: 64, unicode: false),
                        Name = c.String(maxLength: 2000, unicode: false),
                        IsActive = c.Decimal(nullable: false, precision: 1, scale: 0),
                    })
                .PrimaryKey(t => t.Code);
            
            CreateTable(
                "CHEQUEAR.UserRole",
                c => new
                    {
                        UserId = c.String(nullable: false, maxLength: 128, unicode: false),
                        Role = c.String(nullable: false, maxLength: 32, unicode: false),
                    })
                .PrimaryKey(t => new { t.UserId, t.Role })
                .ForeignKey("CHEQUEAR.AppUser", t => t.UserId, cascadeDelete: true)
                .Index(t => t.UserId);
            
            CreateTable(
                "CHEQUEAR.AppUser",
                c => new
                    {
                        Id = c.String(nullable: false, maxLength: 128, unicode: false),
                        DisplayName = c.String(maxLength: 2000, unicode: false),
                        Email = c.String(maxLength: 2000, unicode: false),
                        LastLoginAt = c.DateTime(),
                        LastSyncAt = c.DateTime(nullable: false),
                        IsActive = c.Decimal(nullable: false, precision: 1, scale: 0),
                    })
                .PrimaryKey(t => t.Id);
            
            CreateTable(
                "CHEQUEAR.UserSpecialty",
                c => new
                    {
                        UserId = c.String(nullable: false, maxLength: 128, unicode: false),
                        SpecialtyCode = c.String(nullable: false, maxLength: 64, unicode: false),
                    })
                .PrimaryKey(t => new { t.UserId, t.SpecialtyCode })
                .ForeignKey("CHEQUEAR.AppUser", t => t.UserId, cascadeDelete: true)
                .Index(t => t.UserId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("CHEQUEAR.UserSpecialty", "UserId", "CHEQUEAR.AppUser");
            DropForeignKey("CHEQUEAR.UserRole", "UserId", "CHEQUEAR.AppUser");
            DropForeignKey("CHEQUEAR.QuestionnaireVersion", "QuestionnaireId", "CHEQUEAR.Questionnaire");
            DropForeignKey("CHEQUEAR.Question", "QuestionnaireId", "CHEQUEAR.Questionnaire");
            DropForeignKey("CHEQUEAR.QuestionGroup", "QuestionnaireId", "CHEQUEAR.Questionnaire");
            DropForeignKey("CHEQUEAR.QuestionnaireResponseItem", "ResponseId", "CHEQUEAR.QuestionnaireResponse");
            DropForeignKey("CHEQUEAR.Question", "GroupId", "CHEQUEAR.QuestionGroup");
            DropForeignKey("CHEQUEAR.AnswerOption", "QuestionId", "CHEQUEAR.Question");
            DropIndex("CHEQUEAR.UserSpecialty", new[] { "UserId" });
            DropIndex("CHEQUEAR.UserRole", new[] { "UserId" });
            DropIndex("CHEQUEAR.QuestionnaireVersion", new[] { "QuestionnaireId" });
            DropIndex("CHEQUEAR.QuestionnaireResponseItem", new[] { "ResponseId" });
            DropIndex("CHEQUEAR.Question", new[] { "GroupId" });
            DropIndex("CHEQUEAR.Question", new[] { "QuestionnaireId" });
            DropIndex("CHEQUEAR.QuestionGroup", new[] { "QuestionnaireId" });
            DropIndex("CHEQUEAR.AnswerOption", new[] { "QuestionId" });
            DropTable("CHEQUEAR.UserSpecialty");
            DropTable("CHEQUEAR.AppUser");
            DropTable("CHEQUEAR.UserRole");
            DropTable("CHEQUEAR.Specialty");
            DropTable("CHEQUEAR.SavedReport");
            DropTable("CHEQUEAR.ReportExecution");
            DropTable("CHEQUEAR.RefreshToken");
            DropTable("CHEQUEAR.QuestionnaireVersion");
            DropTable("CHEQUEAR.Questionnaire");
            DropTable("CHEQUEAR.QuestionnaireResponse");
            DropTable("CHEQUEAR.QuestionnaireResponseItem");
            DropTable("CHEQUEAR.Question");
            DropTable("CHEQUEAR.QuestionGroup");
            DropTable("CHEQUEAR.ExportJob");
            DropTable("CHEQUEAR.ExportArtifact");
            DropTable("CHEQUEAR.AuditEvent");
            DropTable("CHEQUEAR.Attachment");
            DropTable("CHEQUEAR.AnswerOption");
        }
    }
}
