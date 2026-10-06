using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Entities;
using ChequeAR.Infrastructure.EF;

namespace ChequeAR.Infrastructure.Repositories
{
    /// <summary>
    /// Es dueño de su contexto: cada instancia recibe uno nuevo (ver ServiceComposition) y lo
    /// libera en Dispose. AuditDispatcher depende de eso para no retener un DbContext de por
    /// vida.
    /// </summary>
    public sealed class AuditRepository : IAuditRepository, IDisposable
    {
        private readonly ChequeARDbContext _ctx;
        public AuditRepository(ChequeARDbContext ctx) { _ctx = ctx; }

        public async Task AddAsync(AuditEvent evt)
        {
            _ctx.AuditEvents.Add(evt);
            await _ctx.SaveChangesAsync();
        }

        public async Task AddManyAsync(IEnumerable<AuditEvent> events)
        {
            _ctx.AuditEvents.AddRange(events);
            await _ctx.SaveChangesAsync();
        }

        public async Task<IReadOnlyList<AuditEvent>> SearchAsync(string agentUserId, string action, System.DateTime? fromUtc, System.DateTime? toUtc, int max)
        {
            IQueryable<AuditEvent> q = _ctx.AuditEvents.AsNoTracking();
            if (!string.IsNullOrEmpty(agentUserId))
            {
                var u = agentUserId.ToLower(CultureInfo.InvariantCulture);
                // x.AgentUserId.ToLower() queda tal cual (sin Invariant): esto es una expresion
                // LINQ-to-Entities que el proveedor de Oracle traduce a LOWER() en SQL; el
                // overload con CultureInfo no es un metodo que EF6 sepa traducir.
#pragma warning disable CA1304, CA1311
                q = q.Where(x => x.AgentUserId.ToLower().Contains(u));
#pragma warning restore CA1304, CA1311
            }
            if (!string.IsNullOrEmpty(action)) q = q.Where(x => x.Action == action);
            if (fromUtc.HasValue) q = q.Where(x => x.Recorded >= fromUtc.Value);
            if (toUtc.HasValue) q = q.Where(x => x.Recorded < toUtc.Value);
            return await q.OrderByDescending(x => x.Recorded).Take(max).ToListAsync();
        }

        public Task<AuditEvent> GetAsync(Guid id) =>
            _ctx.AuditEvents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);

        public async Task<IReadOnlyList<AuditEvent>> ForEntityAsync(string entityRef, bool includeOperations)
        {
            if (string.IsNullOrEmpty(entityRef)) return new List<AuditEvent>();

            IQueryable<AuditEvent> q = _ctx.AuditEvents.AsNoTracking();
            if (includeOperations)
            {
                // StartsWith se traduce a LIKE 'ref$%' ('$' no es comodín de LIKE).
                var withOperation = entityRef + "$";
                q = q.Where(x => x.EntityRef == entityRef || x.EntityRef.StartsWith(withOperation));
            }
            else
            {
                q = q.Where(x => x.EntityRef == entityRef);
            }
            return await q.OrderByDescending(x => x.Recorded).ToListAsync();
        }

        public void Dispose() => _ctx.Dispose();
    }
}
