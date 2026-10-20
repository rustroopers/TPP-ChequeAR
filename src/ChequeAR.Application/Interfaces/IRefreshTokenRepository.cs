using System;
using System.Threading.Tasks;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Application.Interfaces
{
    public interface IRefreshTokenRepository
    {
        Task AddAsync(RefreshToken t);
        Task<RefreshToken> GetByHashAsync(string hash);
        Task RevokeAsync(string hash);

        Task RevokeAllForUserAsync(string userId, DateTime nowUtc);

        Task PurgeExpiredAsync(DateTime before);
    }
}
