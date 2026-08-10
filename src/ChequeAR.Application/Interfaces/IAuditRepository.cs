using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Application.Interfaces
{
    public interface IAuditRepository
    {
        Task AddAsync(AuditEvent evt);
        Task AddManyAsync(IEnumerable<AuditEvent> events);
        Task<IReadOnlyList<AuditEvent>> SearchAsync(string agentUserId, string action, DateTime? fromUtc, DateTime? toUtc, int max);
    }
}
