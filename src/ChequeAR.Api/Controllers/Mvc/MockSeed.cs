using System;
using System.Collections.Generic;
using ChequeAR.Api.Models;

namespace ChequeAR.Api.Controllers.Mvc
{
    /// Seed estático para demo + fallback cuando la DB no está disponible (ej. dev sin migración aplicada).
    /// Los controllers usan estos métodos como respaldo en su bloque catch.
    internal static class MockSeed
    {
        public static List<QuestionnaireListItem> Questionnaires() => new List<QuestionnaireListItem>
        {
            new QuestionnaireListItem { Id = Guid.Parse("11111111-1111-1111-1111-111111111101"), Title = "Cardio basal",     SpecialtyCode = "cardio",  SpecialtyLabel = "Cardiología",  Version = 3, Status = "published", UpdatedAt = "2026-06-08", Author = "Pérez, A." },
            new QuestionnaireListItem { Id = Guid.Parse("11111111-1111-1111-1111-111111111102"), Title = "Neumo control",    SpecialtyCode = "neumo",   SpecialtyLabel = "Neumonología", Version = 1, Status = "draft",     UpdatedAt = "2026-06-09", Author = "Pérez, A." },
            new QuestionnaireListItem { Id = Guid.Parse("11111111-1111-1111-1111-111111111103"), Title = "Onco seguimiento", SpecialtyCode = "onco",    SpecialtyLabel = "Oncología",    Version = 7, Status = "archived",  UpdatedAt = "2026-04-12", Author = "Gómez, R." },
            new QuestionnaireListItem { Id = Guid.Parse("11111111-1111-1111-1111-111111111104"), Title = "Triaje general",   SpecialtyCode = "general", SpecialtyLabel = "General",      Version = 2, Status = "published", UpdatedAt = "2026-05-30", Author = "López, J." }
        };

        public static QuestionnaireDetailViewModel QuestionnaireDetail(Guid id) => new QuestionnaireDetailViewModel
        {
            Id = id,
            Title = "Cardio basal",
            SpecialtyCode = "cardio",
            SpecialtyLabel = "Cardiología",
            Version = 3,
            Status = "published",
            Author = "Pérez, A.",
            UpdatedAt = "2026-06-08",
            Groups = new List<QuestionGroupView>
            {
                new QuestionGroupView { Title = "Datos del paciente", Questions = new List<QuestionView>
                {
                    new QuestionView { LinkId = "edad", Text = "Edad",  Type = "integer", Required = true, Unit = "años" },
                    new QuestionView { LinkId = "sexo", Text = "Sexo",  Type = "choice",  Required = true, Options = new List<string>{"Femenino","Masculino","Otro"} }
                }},
                new QuestionGroupView { Title = "Signos vitales", Questions = new List<QuestionView>
                {
                    new QuestionView { LinkId = "tasis", Text = "TA sistólica",  Type = "decimal", Required = true, Unit = "mmHg" },
                    new QuestionView { LinkId = "tadia", Text = "TA diastólica", Type = "decimal", Required = true, Unit = "mmHg" },
                    new QuestionView { LinkId = "fc",    Text = "FC",            Type = "integer", Required = true, Unit = "lpm" }
                }},
                new QuestionGroupView { Title = "Antecedentes", Questions = new List<QuestionView>
                {
                    new QuestionView { LinkId = "hta", Text = "¿HTA?",      Type = "boolean", Required = true },
                    new QuestionView { LinkId = "dm",  Text = "¿Diabetes?", Type = "boolean", Required = false }
                }}
            }
        };

        public static List<QuestionnaireVersionView> QuestionnaireVersions() => new List<QuestionnaireVersionView>
        {
            new QuestionnaireVersionView { Version = 3, Date = "2026-06-01", Author = "Pérez, A.", Status = "published", ChangeNote = "Agregada pregunta antecedentes familiares" },
            new QuestionnaireVersionView { Version = 2, Date = "2026-05-15", Author = "Pérez, A.", Status = "archived",  ChangeNote = "Validación de rangos" },
            new QuestionnaireVersionView { Version = 1, Date = "2026-04-20", Author = "Gómez, R.", Status = "archived",  ChangeNote = "Versión inicial" }
        };

        public static List<PatientSearchItem> Patients() => new List<PatientSearchItem>
        {
            new PatientSearchItem { HC = "12345", FullName = "García, Ana",  AgeLabel = "8 años",  SpecialtyCode = "cardio", SpecialtyLabel = "Cardiología" },
            new PatientSearchItem { HC = "12346", FullName = "López, Mateo", AgeLabel = "4 años",  SpecialtyCode = "neumo",  SpecialtyLabel = "Neumonología" },
            new PatientSearchItem { HC = "12347", FullName = "Pérez, Lucía", AgeLabel = "11 años", SpecialtyCode = "onco",   SpecialtyLabel = "Oncología" }
        };

        public static PatientTimelineViewModel PatientTimeline(string hc) => new PatientTimelineViewModel
        {
            Patient = new PatientSearchItem { HC = hc, FullName = "García, Ana", AgeLabel = "8 años", SpecialtyCode = "cardio", SpecialtyLabel = "Cardiología" },
            Entries = new List<EncounterTimelineItem>
            {
                new EncounterTimelineItem { Date = "2026-06-09", QuestionnaireTitle = "Cardio basal",       QuestionnaireVersion = 3, Author = "Pérez, A.", EncounterId = Guid.Parse("22222222-2222-2222-2222-222222222201"), Status = "completed" },
                new EncounterTimelineItem { Date = "2026-05-15", QuestionnaireTitle = "Cardio seguimiento", QuestionnaireVersion = 1, Author = "Gómez, R.", EncounterId = Guid.Parse("22222222-2222-2222-2222-222222222202"), Status = "completed" },
                new EncounterTimelineItem { Date = "2026-03-02", QuestionnaireTitle = "Cardio basal",       QuestionnaireVersion = 2, Author = "Pérez, A.", EncounterId = Guid.Parse("22222222-2222-2222-2222-222222222203"), Status = "completed" }
            }
        };

        public static EncounterReadonlyViewModel EncounterReadonly(Guid id) => new EncounterReadonlyViewModel
        {
            EncounterId = id,
            Patient = new PatientSearchItem { HC = "12345", FullName = "García, Ana", AgeLabel = "8 años", SpecialtyCode = "cardio", SpecialtyLabel = "Cardiología" },
            QuestionnaireTitle = "Cardio basal",
            QuestionnaireVersion = 3,
            Author = "Pérez, A.",
            FinalizedAt = "2026-06-09 12:04",
            Groups = new List<QuestionGroupAnswered>
            {
                new QuestionGroupAnswered { Title = "Datos del paciente", Questions = new List<AnsweredQuestion>
                {
                    new AnsweredQuestion { Text = "Edad", Answer = "8", Unit = "años", Prefilled = true },
                    new AnsweredQuestion { Text = "Sexo", Answer = "Femenino" }
                }},
                new QuestionGroupAnswered { Title = "Signos vitales", Questions = new List<AnsweredQuestion>
                {
                    new AnsweredQuestion { Text = "TA sistólica",  Answer = "110", Unit = "mmHg" },
                    new AnsweredQuestion { Text = "TA diastólica", Answer = "70",  Unit = "mmHg" },
                    new AnsweredQuestion { Text = "FC",            Answer = "95",  Unit = "lpm" }
                }}
            }
        };

        public static List<SavedReportItem> SavedReports() => new List<SavedReportItem>
        {
            new SavedReportItem { Id = Guid.Parse("33333333-3333-3333-3333-333333333301"), Name = "HTA pediátrica 2026", QuestionnaireTitle = "Cardio basal",  Author = "Pérez, A.", LastUsedAt = "2026-06-08" },
            new SavedReportItem { Id = Guid.Parse("33333333-3333-3333-3333-333333333302"), Name = "Cohorte EPOC",        QuestionnaireTitle = "Neumo control", Author = "Gómez, R.", LastUsedAt = "2026-05-30" }
        };

        public static ReportPreviewViewModel ReportPreview()
        {
            var model = new ReportPreviewViewModel
            {
                ReportName = "HTA pediátrica 2026",
                Columns = new List<string> { "HC", "Edad", "TA sist.", "HTA" },
                Total = 142,
                Page = 1,
                PageSize = 50
            };
            for (int i = 0; i < 12; i++)
            {
                model.Rows.Add(new ReportPreviewRow
                {
                    Cells = new Dictionary<string, string>
                    {
                        { "HC", "12" + (340 + i) },
                        { "Edad", (5 + (i % 8)).ToString() },
                        { "TA sist.", (115 + (i * 2)).ToString() },
                        { "HTA", i % 2 == 0 ? "Sí" : "No" }
                    }
                });
            }
            return model;
        }

        public static List<AuditEntry> AuditEntries() => new List<AuditEntry>
        {
            new AuditEntry { Id = Guid.Parse("44444444-4444-4444-4444-444444444401"), Timestamp = "2026-06-10 13:42", Username = "aperez", Action = "UPDATE", Resource = "Questionnaire/47", IP = "10.0.12.45" },
            new AuditEntry { Id = Guid.Parse("44444444-4444-4444-4444-444444444402"), Timestamp = "2026-06-10 13:30", Username = "rgomez", Action = "READ",   Resource = "Patient/12345",    IP = "10.0.12.51" },
            new AuditEntry { Id = Guid.Parse("44444444-4444-4444-4444-444444444403"), Timestamp = "2026-06-10 12:11", Username = "aperez", Action = "EXPORT", Resource = "Report/77",        IP = "10.0.12.45" },
            new AuditEntry { Id = Guid.Parse("44444444-4444-4444-4444-444444444404"), Timestamp = "2026-06-10 11:55", Username = "jlopez", Action = "LOGIN",  Resource = "AuthEvent",        IP = "10.0.12.61" }
        };

        public static AuditDetailViewModel AuditDetail(Guid id) => new AuditDetailViewModel
        {
            Id = id,
            Timestamp = "2026-06-10 13:42:11",
            Username = "aperez",
            FullName = "Pérez, Ana",
            Role = "Médico",
            Specialty = "Cardiología",
            Action = "UPDATE",
            Resource = "Questionnaire/47 (Cardio basal v3)",
            IP = "10.0.12.45",
            UserAgent = "Mozilla/5.0 (Win NT 10.0; …) Chrome/124",
            DiffLines = new List<string>
            {
                "- questionGroups[2].questions[5].required: false",
                "+ questionGroups[2].questions[5].required: true"
            }
        };

        public static List<UserListItem> Users() => new List<UserListItem>
        {
            new UserListItem { Username = "aperez", FullName = "Pérez, Ana",  Role = "Médico",  Specialty = "Cardiología",  Active = true },
            new UserListItem { Username = "rgomez", FullName = "Gómez, Raúl", Role = "Referente", Specialty = "Neumonología", Active = true },
            new UserListItem { Username = "jlopez", FullName = "López, Juan", Role = "Admin",   Specialty = "—",            Active = false }
        };

        public static UserDetailViewModel UserDetail(string username) => new UserDetailViewModel
        {
            Username = username,
            FullName = "Pérez, Ana",
            Email = username + "@garrahan.gov.ar",
            DNI = "12345678",
            Active = true,
            CanAccessAudit = false,
            CanCreateReports = true,
            SpecialtyRoles = new List<UserSpecialtyRole>
            {
                new UserSpecialtyRole { SpecialtyCode = "cardio", SpecialtyLabel = "Cardiología",  Role = "Médico" },
                new UserSpecialtyRole { SpecialtyCode = "neumo",  SpecialtyLabel = "Neumonología", Role = "Sin acceso" }
            }
        };

        public static IntegrationsViewModel IntegrationsStatus() => new IntegrationsViewModel
        {
            Integrations = new List<IntegrationStatus>
            {
                new IntegrationStatus { Name = "FHIR Hospital",   Status = "ok",       LastPing = "14:02:11", LatencyP95 = "218 ms", Detail = "" },
                new IntegrationStatus { Name = "Oracle Vistas",   Status = "ok",       LastPing = "14:02:09", LatencyP95 = "96 ms",  Detail = "" },
                new IntegrationStatus { Name = "Auth Hospital",   Status = "ok",       LastPing = "14:02:08", LatencyP95 = "44 ms",  Detail = "" },
                new IntegrationStatus { Name = "Validador FHIR",  Status = "degraded", LastPing = "13:59:01", LatencyP95 = "1.2 s",  Detail = "12 profiles cargados" }
            },
            RecentErrors = new List<string>
            {
                "13:45 · FHIR · 502 al consultar Observation/12345",
                "11:12 · Oracle · timeout vista VW_PACIENTES"
            }
        };

        public static CapabilityStatementViewModel Capability() => new CapabilityStatementViewModel
        {
            FhirVersion = "5.0.0",
            Software = "ChequeAR 1.0",
            Implementation = "Hospital Garrahan / Producción",
            Resources = new List<CapResource>
            {
                new CapResource { Type = "Questionnaire",          Interactions = "read · search · create · update" },
                new CapResource { Type = "QuestionnaireResponse",  Interactions = "read · search · create · update" },
                new CapResource { Type = "Patient",                Interactions = "read · search" },
                new CapResource { Type = "Observation",            Interactions = "read · search" },
                new CapResource { Type = "AuditEvent",             Interactions = "read · search · create" }
            },
            Profiles = new List<string>
            {
                "ChequeARQuestionnaire",
                "ChequeARQuestionnaireResponse"
            }
        };
    }
}
