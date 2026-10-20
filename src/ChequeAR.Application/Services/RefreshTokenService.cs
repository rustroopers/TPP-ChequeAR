using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Application.Services
{
    public class RefreshRotation
    {
        public string UserId { get; set; }

        public string RefreshToken { get; set; }
    }

    public class RefreshTokenService
    {
        private readonly IRefreshTokenRepository _repo;
        private readonly int _ttlHours;

        private readonly IClock _clock;

        public RefreshTokenService(IRefreshTokenRepository repo, int ttlHours = 8, IClock clock = null)
        {
            _repo = repo;
            _ttlHours = ttlHours;
            _clock = clock ?? SystemClock.Instance;
        }

        public Task<string> IssueAsync(string userId, string userAgent, string ip)
            => IssueAsync(userId, userAgent, ip, _clock.UtcNow.AddHours(_ttlHours));

        private async Task<string> IssueAsync(string userId, string userAgent, string ip, DateTime expiresAt)
        {
            var bytes = new byte[64];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            var raw = Convert.ToBase64String(bytes);
            var hash = Hash(raw);
            // Los vencidos ya no sirven para nada; sin esto la tabla crece con cada login.
            await _repo.PurgeExpiredAsync(_clock.UtcNow);
            await _repo.AddAsync(new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TokenHash = hash,
                IssuedAt = _clock.UtcNow,
                ExpiresAt = expiresAt,
                UserAgent = userAgent,
                Ip = ip
            });
            return raw;
        }

        public async Task<RefreshToken> ValidateAsync(string raw)
        {
            var hash = Hash(raw);
            var t = await _repo.GetByHashAsync(hash);
            return t != null && t.IsActive(_clock.UtcNow) ? t : null;
        }

        public async Task<RefreshRotation> RotateAsync(string raw, string userAgent, string ip)
        {
            var now = _clock.UtcNow;
            var t = await _repo.GetByHashAsync(Hash(raw));
            if (t == null || t.ExpiresAt <= now) return null;
            if (t.RevokedAt != null)
            {
                await _repo.RevokeAllForUserAsync(t.UserId, now);
                return null;
            }

            await _repo.RevokeAsync(t.TokenHash);
            var next = await IssueAsync(t.UserId, userAgent, ip, t.ExpiresAt);
            return new RefreshRotation { UserId = t.UserId, RefreshToken = next };
        }

        public Task RevokeAsync(string raw)
        {
            return _repo.RevokeAsync(Hash(raw));
        }

        private static string Hash(string raw)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
                var sb = new StringBuilder(64);
                foreach (var b in bytes) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }
    }
}
