using System.Collections.Generic;
using System.Threading.Tasks;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Application.Interfaces
{
    public interface IUserRepository
    {
        Task<User> GetAsync(string id);
        Task UpsertAsync(User u);
        Task<IReadOnlyList<User>> ListAsync(string searchTerm, string roleFilter, string specialtyFilter, bool? active, int skip, int take);
        Task<int> CountAsync(string searchTerm, string roleFilter, string specialtyFilter, bool? active);
    }
}
