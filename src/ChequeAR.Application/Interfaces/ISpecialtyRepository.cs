using System.Collections.Generic;
using System.Threading.Tasks;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Application.Interfaces
{
    public interface ISpecialtyRepository
    {
        /// <param name="includeInactive">false => sólo IsActive.</param>
        Task<IReadOnlyList<Specialty>> ListAsync(bool includeInactive = false);
        Task<Specialty> GetAsync(string code);
    }
}
