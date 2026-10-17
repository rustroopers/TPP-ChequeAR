using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Data.Entity.Infrastructure;
using ChequeAR.Application.Exceptions;
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

        public async Task<IReadOnlyList<ClinicalVariable>> ListAsync(string search, QuestionType? type, bool? active, int skip, int take, bool publishedOnly = false)
        {
            // Include primero: en EF6 un Include encadenado después de Skip/Take se ignora en
            // silencio y las opciones vuelven vacías.
            IQueryable<ClinicalVariable> q = Filter(search, type, active, publishedOnly)
                .Include(v => v.Options)
                .OrderByDescending(v => v.CreatedAt).ThenBy(v => v.Name);
            if (skip > 0) q = q.Skip(skip);
            return await q.Take(take <= 0 ? 50 : take).ToListAsync();
        }

        public Task<int> CountAsync(string search, QuestionType? type, bool? active, bool publishedOnly = false) =>
            Filter(search, type, active, publishedOnly).CountAsync();

        private IQueryable<ClinicalVariable> Filter(string search, QuestionType? type, bool? active, bool publishedOnly)
        {
            var q = _ctx.ClinicalVariables.AsQueryable();
            if (publishedOnly) q = q.Where(v => v.CurrentVersionId != null);
            if (active.HasValue) q = q.Where(v => v.IsActive == active.Value);
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
                await SaveAsync(v);

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

        public Task UpdateHeaderAsync(ClinicalVariable v) => SaveAsync(v);

        public async Task AddVersionAsync(ClinicalVariable v, ClinicalVariableVersion version)
        {
            _ctx.ClinicalVariableVersions.Add(version);
            await SaveAsync(v);
        }

        public Task<ClinicalVariableVersion> GetVersionAsync(Guid versionId) =>
            _ctx.ClinicalVariableVersions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == versionId);

        public Task<ClinicalVariableVersion> GetVersionAsync(Guid variableId, int versionNumber) =>
            _ctx.ClinicalVariableVersions.AsNoTracking()
                .FirstOrDefaultAsync(x => x.VariableId == variableId && x.VersionNumber == versionNumber);

        public async Task<IReadOnlyList<ClinicalVariableVersion>> ListVersionsAsync(Guid variableId)
        {
            var rows = await _ctx.ClinicalVariableVersions.AsNoTracking()
                .Where(x => x.VariableId == variableId)
                .OrderByDescending(x => x.VersionNumber)
                .Select(x => new { x.Id, x.VariableId, x.VersionNumber, x.SourceRevision, x.PublishedBy, x.PublishedAt, x.ChangeNote })
                .ToListAsync();
            return rows.Select(x => new ClinicalVariableVersion
            {
                Id = x.Id, VariableId = x.VariableId, VersionNumber = x.VersionNumber, SourceRevision = x.SourceRevision,
                PublishedBy = x.PublishedBy, PublishedAt = x.PublishedAt, ChangeNote = x.ChangeNote
            }).ToList();
        }

        public async Task<IReadOnlyList<ClinicalVariableVersion>> GetVersionsAsync(IEnumerable<Guid> variableIds)
        {
            var ids = variableIds?.Distinct().ToList() ?? new List<Guid>();
            if (ids.Count == 0) return new List<ClinicalVariableVersion>();
            return await _ctx.ClinicalVariableVersions.AsNoTracking().Where(x => ids.Contains(x.VariableId)).ToListAsync();
        }

        public async Task<IReadOnlyDictionary<Guid, ClinicalVariableVersion>> GetCurrentVersionHeadersAsync(IEnumerable<Guid> variableIds)
        {
            var ids = variableIds?.Distinct().ToList() ?? new List<Guid>();
            if (ids.Count == 0) return new Dictionary<Guid, ClinicalVariableVersion>();
            var rows = await (from v in _ctx.ClinicalVariables
                              where ids.Contains(v.Id)
                              join cv in _ctx.ClinicalVariableVersions on v.CurrentVersionId equals cv.Id
                              select new { cv.Id, cv.VariableId, cv.VersionNumber, cv.SourceRevision }).ToListAsync();
            return rows.ToDictionary(x => x.VariableId, x => new ClinicalVariableVersion
            {
                Id = x.Id, VariableId = x.VariableId, VersionNumber = x.VersionNumber, SourceRevision = x.SourceRevision
            });
        }

        public async Task UpdateVersionSourceAsync(ClinicalVariableVersion version)
        {
            var row = await _ctx.ClinicalVariableVersions.FirstOrDefaultAsync(x => x.Id == version.Id);
            if (row == null) return;
            row.SourceRevision = version.SourceRevision;
            await _ctx.SaveChangesAsync();
        }

        /// <summary>
        /// Revision es el token de concurrencia: si otro guardó en el medio, el UPDATE no afecta
        /// filas. Se traduce igual que en QuestionnaireRepository.
        /// </summary>
        private async Task SaveAsync(ClinicalVariable v)
        {
            try
            {
                await _ctx.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                var actual = await _ctx.ClinicalVariables.AsNoTracking()
                    .Where(x => x.Id == v.Id).Select(x => (int?)x.Revision).FirstOrDefaultAsync();
                throw new ConcurrencyConflictException(v.Revision, actual ?? v.Revision,
                    "Otra persona modificó la variable mientras usted la editaba. Vuelva a abrirla para ver sus cambios.");
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
