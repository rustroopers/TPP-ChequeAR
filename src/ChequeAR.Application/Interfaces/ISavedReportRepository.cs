using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Application.Interfaces
{
    public interface ISavedReportRepository
    {
        Task<SavedReport> GetAsync(Guid id);
        Task<IReadOnlyList<SavedReport>> ListForUserAsync(string userId, IEnumerable<string> specialties);
        Task AddAsync(SavedReport r);
        Task UpdateAsync(SavedReport r);
        Task DeleteAsync(Guid id);
    }
}
