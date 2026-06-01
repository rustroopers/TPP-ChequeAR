using System;
using System.Collections.Generic;

namespace ChequeAR.Domain.Entities
{
    public class User
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string Email { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public DateTime LastSyncAt { get; set; }
        public bool IsActive { get; set; }

        public virtual ICollection<UserRole> Roles { get; set; } = new List<UserRole>();
        public virtual ICollection<UserSpecialty> Specialties { get; set; } = new List<UserSpecialty>();
    }
}
