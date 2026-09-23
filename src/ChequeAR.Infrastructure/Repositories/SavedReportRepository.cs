using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Entities;
using ChequeAR.Infrastructure.EF;

namespace ChequeAR.Infrastructure.Repositories
{
    public class SavedReportRepository : ISavedReportRepository
    {
        private readonly ChequeARDbContext _ctx;
        public SavedReportRepository(ChequeARDbContext ctx) { _ctx = ctx; }

        public Task<SavedReport> GetAsync(Guid id) =>
            _ctx.SavedReports.FirstOrDefaultAsync(r => r.Id == id);

        public async Task<IReadOnlyList<SavedReport>> ListForUserAsync(string userId, IEnumerable<string> specialties)
        {
            IQueryable<SavedReport> query;
            if (specialties == null)
            {
                // Sin restricción: además de lo propio, CUALQUIER reporte compartido a nivel
                // especialidad (ver ISavedReportRepository.ListForUserAsync).
                query = _ctx.SavedReports.Where(r => r.CreatedBy == userId || r.Scope == "specialty");
            }
            else
            {
                // Materializada a List<T> antes de entrar al árbol de expresión: el
                // proveedor de EF6 no siempre traduce Contains sobre un IEnumerable
                // arbitrario (p. ej. un HashSet, que es lo que da SpecialtyScope.Codes).
                var specs = specialties.ToList();
                query = _ctx.SavedReports.Where(r => r.CreatedBy == userId
                    || (r.Scope == "specialty" && specs.Contains(r.SpecialtyCode)));
            }

            return await query.OrderByDescending(r => r.UpdatedAt).ToListAsync();
        }

        public async Task AddAsync(SavedReport r) { _ctx.SavedReports.Add(r); await _ctx.SaveChangesAsync(); }
        public Task UpdateAsync(SavedReport r) => _ctx.SaveChangesAsync();
        public async Task DeleteAsync(Guid id)
        {
            var r = await _ctx.SavedReports.FindAsync(id);
            if (r == null) return;
            _ctx.SavedReports.Remove(r);
            await _ctx.SaveChangesAsync();
        }
    }
}
