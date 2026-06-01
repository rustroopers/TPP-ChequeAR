using System.Threading.Tasks;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Application.Interfaces
{
    public interface IUserRepository
    {
        Task<User> GetAsync(string id);
        Task UpsertAsync(User u);
    }
}
