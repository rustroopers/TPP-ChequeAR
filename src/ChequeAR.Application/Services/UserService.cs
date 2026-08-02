using System.Collections.Generic;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Application.Services
{
    /// <summary>
    /// Gestión del espejo del padrón hospitalario.
    ///
    /// Es fina a propósito: hoy sólo delega. Existe igual porque la capa de presentación no
    /// debe conocer la de datos —el día que aparezca una regla sobre altas o roles, tiene
    /// dónde vivir en vez de terminar en un controller.
    /// </summary>
    public class UserService
    {
        private readonly IUserRepository _repo;

        public UserService(IUserRepository repo) { _repo = repo; }

        public Task<User> GetAsync(string id) => _repo.GetAsync(id);

        public async Task<UserPage> ListAsync(
            string searchTerm = null, string role = null, string specialty = null,
            bool? active = null, int skip = 0, int take = 50)
        {
            var items = await _repo.ListAsync(searchTerm, role, specialty, active, skip, take);
            var total = await _repo.CountAsync(searchTerm, role, specialty, active);
            return new UserPage { Total = total, Items = items };
        }

        public Task UpsertAsync(User u) => _repo.UpsertAsync(u);
    }

    public class UserPage
    {
        public int Total { get; set; }
        public IReadOnlyList<User> Items { get; set; } = new List<User>();
    }
}
