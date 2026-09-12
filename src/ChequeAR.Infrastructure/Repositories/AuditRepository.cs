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
    public class AuditRepository : IAuditRepository
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
            var q = _ctx.AuditEvents.AsQueryable();
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
            return await q.OrderByDescending(x => x.Recorded).Skip(0).Take(max).ToListAsync();
        }
    }
}
