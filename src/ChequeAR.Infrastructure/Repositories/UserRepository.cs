using System.Data.Entity;
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
    }
}
