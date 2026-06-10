using System.Collections.Generic;

namespace ChequeAR.Api.Models
{
    public class AuditEntry
    {
        public long Id { get; set; }
        public string Timestamp { get; set; }
        public string Username { get; set; }
        public string Action { get; set; }
        public string Resource { get; set; }
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
        public long Id { get; set; }
        public string Timestamp { get; set; }
        public string Username { get; set; }
        public string FullName { get; set; }
        public string Role { get; set; }
        public string Specialty { get; set; }
        public string Action { get; set; }
        public string Resource { get; set; }
        public string IP { get; set; }
        public string UserAgent { get; set; }
        public List<string> DiffLines { get; set; } = new List<string>();
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

    public class IntegrationStatus
    {
        public string Name { get; set; }
        public string Status { get; set; }   // ok | degraded | down
        public string LastPing { get; set; }
        public string LatencyP95 { get; set; }
        public string Detail { get; set; }
    }

    public class IntegrationsViewModel
    {
        public List<IntegrationStatus> Integrations { get; set; } = new List<IntegrationStatus>();
        public List<string> RecentErrors { get; set; } = new List<string>();
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
