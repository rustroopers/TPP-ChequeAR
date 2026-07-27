using System;
using System.Collections.Generic;

namespace ChequeAR.Api.Models
{
    public class AuditEntry
    {
        public Guid Id { get; set; }
        public string Timestamp { get; set; }
        public string Username { get; set; }
        public string Action { get; set; }        // etiqueta legible en español
        public string ActionBadge { get; set; }   // clase css del badge (color)
        public string ActionIcon { get; set; }    // clase del ícono bootstrap
        public string Resource { get; set; }       // título del cuestionario o ref limpia
        public string IP { get; set; }
    }

    public class AuditListViewModel
    {
        public string UserFilter { get; set; }
        public string ActionFilter { get; set; }
        public string ResourceFilter { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public List<AuditEntry> Entries { get; set; } = new List<AuditEntry>();
        public int Total { get; set; }
        public int Page { get; set; }
    }

    public class AuditDetailViewModel
    {
        public Guid Id { get; set; }
        public string Timestamp { get; set; }
        public string Username { get; set; }
        public string FullName { get; set; }
        public string Role { get; set; }
        public string Specialty { get; set; }
        public string Action { get; set; }
        public string ActionBadge { get; set; }
        public string ActionIcon { get; set; }
        public string Resource { get; set; }
        public string IP { get; set; }
        public string UserAgent { get; set; }

        // Diff de versiones (solo para publicaciones de un cuestionario).
        public bool HasVersionDiff { get; set; }
        public int? FromVersion { get; set; }   // null si fue la primera versión
        public int? ToVersion { get; set; }
        public List<Helpers.AuditPresentation.DiffLine> BeforeLines { get; set; } = new List<Helpers.AuditPresentation.DiffLine>();
        public List<Helpers.AuditPresentation.DiffLine> AfterLines { get; set; } = new List<Helpers.AuditPresentation.DiffLine>();
    }

    public class UserListItem
    {
        public string Username { get; set; }
        public string FullName { get; set; }
        public string Role { get; set; }
        public string Specialty { get; set; }
        public bool Active { get; set; }
    }

    public class UserListViewModel
    {
        public List<UserListItem> Users { get; set; } = new List<UserListItem>();
    }

    public class UserDetailViewModel
    {
        public string Username { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }
        public string DNI { get; set; }
        public bool Active { get; set; }
        public List<UserSpecialtyRole> SpecialtyRoles { get; set; } = new List<UserSpecialtyRole>();
        public bool CanAccessAudit { get; set; }
        public bool CanCreateReports { get; set; }
    }

    public class UserSpecialtyRole
    {
        public string SpecialtyCode { get; set; }
        public string SpecialtyLabel { get; set; }
        public string Role { get; set; }
    }

    public class CapabilityStatementViewModel
    {
        public string FhirVersion { get; set; }
        public string Software { get; set; }
        public string Implementation { get; set; }
        public List<CapResource> Resources { get; set; } = new List<CapResource>();
        public List<string> Profiles { get; set; } = new List<string>();
    }

    public class CapResource
    {
        public string Type { get; set; }
        public string Interactions { get; set; }
    }
}
