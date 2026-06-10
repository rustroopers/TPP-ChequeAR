using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Entities;
using ChequeAR.Infrastructure.EF;

namespace ChequeAR.Infrastructure.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly ChequeARDbContext _ctx;
        public UserRepository(ChequeARDbContext ctx) { _ctx = ctx; }

        public Task<User> GetAsync(string id) =>
            _ctx.Users.Include(u => u.Roles).Include(u => u.Specialties).FirstOrDefaultAsync(u => u.Id == id);

        public async Task UpsertAsync(User u)
        {
            var existing = await _ctx.Users.FindAsync(u.Id);
            if (existing == null) _ctx.Users.Add(u);
            else _ctx.Entry(existing).CurrentValues.SetValues(u);
            await _ctx.SaveChangesAsync();
        }

        public async Task<IReadOnlyList<User>> ListAsync(string searchTerm, string roleFilter, string specialtyFilter, bool? active, int skip, int take)
        {
            var q = _ctx.Users.Include(u => u.Roles).Include(u => u.Specialties).AsQueryable();
            if (!string.IsNullOrEmpty(searchTerm))
            {
                q = q.Where(u => u.DisplayName.Contains(searchTerm) || u.Id.Contains(searchTerm) || u.Email.Contains(searchTerm));
            }
            if (!string.IsNullOrEmpty(roleFilter))
            {
                q = q.Where(u => u.Roles.Any(r => r.Role == roleFilter));
            }
            if (!string.IsNullOrEmpty(specialtyFilter))
            {
                q = q.Where(u => u.Specialties.Any(s => s.SpecialtyCode == specialtyFilter));
            }
            if (active.HasValue)
            {
                q = q.Where(u => u.IsActive == active.Value);
            }
            return await q.OrderBy(u => u.DisplayName).Skip(skip).Take(take).ToListAsync();
        }

        public Task<int> CountAsync(string searchTerm, string roleFilter, string specialtyFilter, bool? active)
        {
            var q = _ctx.Users.AsQueryable();
            if (!string.IsNullOrEmpty(searchTerm))
                q = q.Where(u => u.DisplayName.Contains(searchTerm) || u.Id.Contains(searchTerm) || u.Email.Contains(searchTerm));
            if (!string.IsNullOrEmpty(roleFilter))
                q = q.Where(u => u.Roles.Any(r => r.Role == roleFilter));
            if (!string.IsNullOrEmpty(specialtyFilter))
                q = q.Where(u => u.Specialties.Any(s => s.SpecialtyCode == specialtyFilter));
            if (active.HasValue)
                q = q.Where(u => u.IsActive == active.Value);
            return q.CountAsync();
        }
    }
}
