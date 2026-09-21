using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Application.Interfaces
{
    public interface ISavedReportRepository
    {
        Task<SavedReport> GetAsync(Guid id);

        /// <param name="specialties">
        /// null = sin restricción (Administrador, o el flag de scope apagado): además de lo
        /// propio, trae todo reporte Scope="specialty" sin importar el código. Una lista
        /// (incluso vacía) = restringido a exactamente esos códigos, además de lo propio.
        /// </param>
        Task<IReadOnlyList<SavedReport>> ListForUserAsync(string userId, IEnumerable<string> specialties);
        Task AddAsync(SavedReport r);
        Task UpdateAsync(SavedReport r);
        Task DeleteAsync(Guid id);
    }
}
