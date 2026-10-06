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

        /// <summary>Null si no existe.</summary>
        Task<AuditEvent> GetAsync(Guid id);

        /// <summary>
        /// Eventos cuyo EntityRef es exactamente <paramref name="entityRef"/> y, si
        /// <paramref name="includeOperations"/>, también los de sus operaciones
        /// (<c>{entityRef}$save</c>, <c>{entityRef}$finalize</c>, …). Sin tope de filas:
        /// el historial de un recurso está acotado por naturaleza.
        /// </summary>
        Task<IReadOnlyList<AuditEvent>> ForEntityAsync(string entityRef, bool includeOperations);
    }
}
