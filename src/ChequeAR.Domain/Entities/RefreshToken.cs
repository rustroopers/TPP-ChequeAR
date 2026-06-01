using System;

namespace ChequeAR.Domain.Entities
{
    public class RefreshToken
    {
        public Guid Id { get; set; }
        public string UserId { get; set; }
        public string TokenHash { get; set; }
        public DateTime IssuedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime? RevokedAt { get; set; }
        public string UserAgent { get; set; }
        public string Ip { get; set; }

        public bool IsActive()
        {
            return RevokedAt == null && ExpiresAt > DateTime.UtcNow;
        }
    }
}
