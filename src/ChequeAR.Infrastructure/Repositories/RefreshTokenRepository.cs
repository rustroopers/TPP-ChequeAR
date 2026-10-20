using System;
using System.Data.Entity;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Entities;
using ChequeAR.Infrastructure.EF;

namespace ChequeAR.Infrastructure.Repositories
{
    public class RefreshTokenRepository : IRefreshTokenRepository
    {
        private readonly ChequeARDbContext _ctx;
        public RefreshTokenRepository(ChequeARDbContext ctx) { _ctx = ctx; }

        public async Task AddAsync(RefreshToken t)
        {
            _ctx.RefreshTokens.Add(t);
            await _ctx.SaveChangesAsync();
        }

        public Task<RefreshToken> GetByHashAsync(string hash) =>
            _ctx.RefreshTokens.FirstOrDefaultAsync(x => x.TokenHash == hash);

        public Task PurgeExpiredAsync(DateTime before) =>
            _ctx.Database.ExecuteSqlCommandAsync(@"DELETE FROM ""CHEQUEAR"".""RefreshToken"" WHERE ""ExpiresAt"" < :p0", before);

        public Task RevokeAllForUserAsync(string userId, DateTime nowUtc) =>
            _ctx.Database.ExecuteSqlCommandAsync(
                @"UPDATE ""CHEQUEAR"".""RefreshToken"" SET ""RevokedAt"" = :p0 WHERE ""UserId"" = :p1 AND ""RevokedAt"" IS NULL AND ""ExpiresAt"" > :p2",
                nowUtc, userId, nowUtc);

        public async Task RevokeAsync(string hash)
        {
            var t = await _ctx.RefreshTokens.FirstOrDefaultAsync(x => x.TokenHash == hash);
            if (t == null) return;
            t.RevokedAt = DateTime.UtcNow;
            await _ctx.SaveChangesAsync();
        }
    }
}
