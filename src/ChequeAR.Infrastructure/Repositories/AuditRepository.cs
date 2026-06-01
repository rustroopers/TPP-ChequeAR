using System.Collections.Generic;
using System.Data.Entity;
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

        public async Task<IReadOnlyList<AuditEvent>> SearchAsync(string agentUserId, string entityRef, int skip, int take)
        {
            var q = _ctx.AuditEvents.AsQueryable();
            if (!string.IsNullOrEmpty(agentUserId)) q = q.Where(x => x.AgentUserId == agentUserId);
            if (!string.IsNullOrEmpty(entityRef)) q = q.Where(x => x.EntityRef == entityRef);
            return await q.OrderByDescending(x => x.Recorded).Skip(skip).Take(take).ToListAsync();
        }
    }
}
