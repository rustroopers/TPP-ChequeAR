using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using ChequeAR.Infrastructure.EF;

namespace ChequeAR.Infrastructure.Repositories
{
    public class ClinicalVariableRepository : IClinicalVariableRepository
    {
        private readonly ChequeARDbContext _ctx;

        public ClinicalVariableRepository(ChequeARDbContext ctx) { _ctx = ctx; }

        public Task<ClinicalVariable> GetAsync(Guid id) =>
            _ctx.ClinicalVariables.Include(v => v.Options).FirstOrDefaultAsync(v => v.Id == id);

        public Task<ClinicalVariable> GetByCodeAsync(string code) =>
            _ctx.ClinicalVariables.Include(v => v.Options).FirstOrDefaultAsync(v => v.Code == code);

        public Task<ClinicalVariable> GetBySnomedCodeAsync(string snomedCode) =>
            _ctx.ClinicalVariables.Include(v => v.Options).FirstOrDefaultAsync(v => v.SnomedCode == snomedCode);

        public async Task<IReadOnlyList<ClinicalVariable>> GetManyAsync(IEnumerable<Guid> ids)
        {
            var list = ids?.Distinct().ToList() ?? new List<Guid>();
            if (list.Count == 0) return new List<ClinicalVariable>();
            return await _ctx.ClinicalVariables
                .Include(v => v.Options)
                .Where(v => list.Contains(v.Id))
                .ToListAsync();
        }

        public async Task<IReadOnlyList<ClinicalVariable>> ListAsync(string search, QuestionType? type, bool includeInactive, int skip, int take)
        {
            // Include primero: en EF6 un Include encadenado después de Skip/Take se ignora en
            // silencio y las opciones vuelven vacías.
            IQueryable<ClinicalVariable> q = Filter(search, type, includeInactive)
                .Include(v => v.Options)
                .OrderBy(v => v.SortOrder).ThenBy(v => v.Name);
            if (skip > 0) q = q.Skip(skip);
            return await q.Take(take <= 0 ? 50 : take).ToListAsync();
        }

        public Task<int> CountAsync(string search, QuestionType? type, bool includeInactive) =>
            Filter(search, type, includeInactive).CountAsync();

        private IQueryable<ClinicalVariable> Filter(string search, QuestionType? type, bool includeInactive)
        {
            var q = _ctx.ClinicalVariables.AsQueryable();
            if (!includeInactive) q = q.Where(v => v.IsActive);
            if (type.HasValue) q = q.Where(v => v.Type == type.Value);
            if (!string.IsNullOrWhiteSpace(search))
            {
                // Un solo campo de búsqueda: el usuario escribe "peso" o pega un código SNOMED
                // y no tiene por qué declarar cuál de los dos es.
                // v.Name.ToLower() (sin Invariant) es una expresión LINQ-to-Entities que el
                // proveedor de Oracle traduce a LOWER(); el overload con CultureInfo no es un
                // método que EF6 sepa traducir.
#pragma warning disable CA1304, CA1311
                var s = search.Trim().ToLower(CultureInfo.InvariantCulture);
                q = q.Where(v => v.Name.ToLower().Contains(s)
                              || v.Code.ToLower().Contains(s)
                              || (v.SnomedCode != null && v.SnomedCode.Contains(s))
                              || (v.SnomedDisplay != null && v.SnomedDisplay.ToLower().Contains(s)));
#pragma warning restore CA1304, CA1311
            }
            return q;
        }

        public async Task AddAsync(ClinicalVariable v)
        {
            _ctx.ClinicalVariables.Add(v);
            await _ctx.SaveChangesAsync();
        }

        public async Task UpdateWithOptionsAsync(ClinicalVariable v, ICollection<VariableOption> options)
        {
            // Mismo patrón que QuestionnaireRepository.UpdateWithStructureAsync: la cabecera y
            // el reemplazo de hijos en una sola transacción, para que un fallo a mitad de camino
            // no deje la variable actualizada con las opciones viejas a medio borrar.
            using (var tx = _ctx.Database.BeginTransaction())
            {
                await _ctx.SaveChangesAsync();

                // Borrado explícito: la FK está con WillCascadeOnDelete(false).
                var old = await _ctx.VariableOptions.Where(o => o.VariableId == v.Id).ToListAsync();
                _ctx.VariableOptions.RemoveRange(old);
                // La colección de la entidad trackeada apunta a las filas recién borradas:
                // vaciarla evita que EF intente re-insertarlas al guardar.
                v.Options.Clear();

                var i = 0;
                foreach (var o in options ?? new List<VariableOption>())
                {
                    if (o.Id == Guid.Empty) o.Id = Guid.NewGuid();
                    o.VariableId = v.Id;
                    o.OrderIndex = i++;
                    _ctx.VariableOptions.Add(o);
                }

                await _ctx.SaveChangesAsync();
                tx.Commit();
            }
        }

        public async Task DeleteAsync(Guid id)
        {
            using (var tx = _ctx.Database.BeginTransaction())
            {
                var options = await _ctx.VariableOptions.Where(o => o.VariableId == id).ToListAsync();
                _ctx.VariableOptions.RemoveRange(options);

                var v = await _ctx.ClinicalVariables.FirstOrDefaultAsync(x => x.Id == id);
                if (v != null) _ctx.ClinicalVariables.Remove(v);

                await _ctx.SaveChangesAsync();
                tx.Commit();
            }
        }

        public Task<int> CountQuestionsUsingAsync(Guid variableId) =>
            _ctx.Questions.CountAsync(q => q.VariableId == variableId);

        public async Task<IReadOnlyList<Guid>> QuestionnaireIdsUsingAsync(Guid variableId) =>
            await _ctx.Questions
                .Where(q => q.VariableId == variableId)
                .Select(q => q.QuestionnaireId)
                .Distinct()
                .ToListAsync();
    }
}
