using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Application.Services
{
    public class RefreshTokenService
    {
        private readonly IRefreshTokenRepository _repo;
        private readonly int _ttlHours;

        public RefreshTokenService(IRefreshTokenRepository repo, int ttlHours = 8)
        {
            _repo = repo;
            _ttlHours = ttlHours;
        }

        public async Task<string> IssueAsync(string userId, string userAgent, string ip)
        {
            var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            var hash = Hash(raw);
            await _repo.AddAsync(new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TokenHash = hash,
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(_ttlHours),
                UserAgent = userAgent,
                Ip = ip
            });
            return raw;
        }

        public async Task<RefreshToken> ValidateAsync(string raw)
        {
            var hash = Hash(raw);
            var t = await _repo.GetByHashAsync(hash);
            return t != null && t.IsActive() ? t : null;
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
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
