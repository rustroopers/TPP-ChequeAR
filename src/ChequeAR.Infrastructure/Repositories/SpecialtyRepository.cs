using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Entities;
using ChequeAR.Infrastructure.EF;

namespace ChequeAR.Infrastructure.Repositories
{
    public class SpecialtyRepository : ISpecialtyRepository
    {
        private readonly ChequeARDbContext _ctx;
        public SpecialtyRepository(ChequeARDbContext ctx) { _ctx = ctx; }

        public async Task<IReadOnlyList<Specialty>> ListAsync(bool includeInactive = false)
        {
            var q = _ctx.Specialties.AsQueryable();
            if (!includeInactive) q = q.Where(s => s.IsActive);
            return await q.OrderBy(s => s.SortOrder).ThenBy(s => s.Name).ToListAsync();
        }

        public Task<Specialty> GetAsync(string code) =>
            _ctx.Specialties.FirstOrDefaultAsync(s => s.Code == code);
    }
}
