using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Application.Interfaces
{
    public interface IAuditRepository
    {
        Task AddAsync(AuditEvent evt);
        Task AddManyAsync(IEnumerable<AuditEvent> events);

        /// <summary>
        /// Una página de la búsqueda, del evento más reciente al más viejo, con el total de
        /// coincidencias. Filtra, cuenta y pagina en la base. Los eventos vuelven sin
        /// <c>FhirJson</c> salvo los de consultas guardadas, cuyo nombre sale de ahí: un
        /// listado no necesita la foto completa de cada ficha.
        /// </summary>
        Task<AuditPage> SearchAsync(AuditSearchCriteria criteria, int skip, int take);

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
