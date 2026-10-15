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

        /// <summary>
        /// Hash PBKDF2 de la contraseña para el login local (ver
        /// ChequeAR.Application.Security.PasswordHasher/LocalCredentialAuthenticator).
        /// Null = este usuario no puede loguearse localmente (fail-closed) -- es el estado
        /// normal de un usuario que sólo entra vía HospitalAuthClient cuando ese modo esté
        /// activo.
        /// </summary>
        public string PasswordHash { get; set; }
        public DateTime? PasswordUpdatedAt { get; set; }

        /// <summary>
        /// Se compara con el de la cookie y el JWT; rotarlo cierra todas las sesiones. Null hasta
        /// el primer login.
        /// </summary>
        public string SecurityStamp { get; set; }

        public int FailedLoginCount { get; set; }

        /// <summary>UTC.</summary>
        public DateTime? LockoutUntil { get; set; }

        public virtual ICollection<UserRole> Roles { get; set; } = new List<UserRole>();
        public virtual ICollection<UserSpecialty> Specialties { get; set; } = new List<UserSpecialty>();
    }
}
