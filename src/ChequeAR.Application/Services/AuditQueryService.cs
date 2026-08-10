using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Application.Services
{
    /// <summary>
    /// Consultas sobre el registro de auditoría.
    ///
    /// Esta lógica vivía repartida en dos controllers, que traían 5000 filas y filtraban y
    /// paginaban en memoria. Además de estar en la capa equivocada, ese código no tenía
    /// ninguna prueba: no se puede probar filtrado y paginación sin instanciar la capa web.
    /// </summary>
    public class AuditQueryService
    {
        /// <summary>
        /// Tope de filas que se traen para filtrar. Es el comportamiento que ya existía, hecho
        /// explícito: por encima de esto la auditoría necesita filtrado del lado de la base
        /// (IAuditRepository con más parámetros), no un número más grande acá.
        /// </summary>
        public const int MaxFilas = 5000;

        private readonly IAuditRepository _repo;

        public AuditQueryService(IAuditRepository repo) { _repo = repo; }

        /// <param name="entityRef">Recurso afectado, ej. "Questionnaire/{id}".</param>
        public async Task<AuditPage> SearchAsync(
            string agentUserId = null, string action = null, string entityRef = null,
            DateTime? fromUtc = null, DateTime? toUtc = null, int skip = 0, int take = 50)
        {
            var todos = await _repo.SearchAsync(agentUserId, action, fromUtc, toUtc, MaxFilas);

            IEnumerable<AuditEvent> filtrados = todos ?? new List<AuditEvent>();
            if (!string.IsNullOrEmpty(entityRef))
                filtrados = filtrados.Where(x => x.EntityRef == entityRef);

            var lista = filtrados.ToList();

            return new AuditPage
            {
                // El total es el de la búsqueda completa, no el de la página: con el de la
                // página, la última parece la única y el paginador desaparece.
                Total = lista.Count,
                Items = lista.Skip(Math.Max(0, skip)).Take(Math.Max(1, take)).ToList()
            };
        }

        public async Task<AuditEvent> GetAsync(Guid id)
        {
            // El repositorio no expone búsqueda por identificador; se filtra sobre la ventana
            // amplia. Optimización futura: IAuditRepository.GetAsync(id).
            var todos = await _repo.SearchAsync(null, null, null, null, MaxFilas);
            return (todos ?? new List<AuditEvent>()).FirstOrDefault(e => e.Id == id);
        }
    }

    public class AuditPage
    {
        public int Total { get; set; }
        public IReadOnlyList<AuditEvent> Items { get; set; } = new List<AuditEvent>();
    }
}
