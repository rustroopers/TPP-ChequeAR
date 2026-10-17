using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Entities;
using ChequeAR.Infrastructure.EF;

namespace ChequeAR.Infrastructure.Repositories
{
    public sealed class SpecialtyRepository : ISpecialtyRepository, System.IDisposable
    {
        private readonly ChequeARDbContext _ctx;
        private readonly bool _ownsContext;
        public SpecialtyRepository(ChequeARDbContext ctx, bool ownsContext) { _ctx = ctx; _ownsContext = ownsContext; }

        public void Dispose()
        {
            if (_ownsContext) _ctx.Dispose();
        }

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
