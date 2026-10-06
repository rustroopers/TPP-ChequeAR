using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Application.Services
{
    /// <summary>Consultas sobre el registro de auditoría.</summary>
    public class AuditQueryService
    {
        /// <summary>
        /// Tope de filas del listado GLOBAL (sin recurso): los eventos más recientes que se
        /// traen para filtrar y paginar. Las búsquedas por recurso o por identificador no
        /// tienen tope: van directo a la base.
        /// </summary>
        public const int MaxFilas = 5000;

        private readonly IAuditRepository _repo;

        public AuditQueryService(IAuditRepository repo) { _repo = repo; }

        /// <param name="entityRef">Recurso afectado, ej. "Questionnaire/{id}".</param>
        public async Task<AuditPage> SearchAsync(
            string agentUserId = null, string action = null, string entityRef = null,
            DateTime? fromUtc = null, DateTime? toUtc = null, int skip = 0, int take = 50)
        {
            IEnumerable<AuditEvent> filtrados;
            if (!string.IsNullOrEmpty(entityRef))
            {
                // Con recurso, la base devuelve SOLO sus eventos (sin la ventana de los últimos
                // MaxFilas, que dejaba afuera a los recursos viejos) y el resto de los
                // filtros se aplica sobre ese conjunto acotado, con la misma semántica que
                // IAuditRepository.SearchAsync.
                var delRecurso = await _repo.ForEntityAsync(entityRef, includeOperations: false)
                                 ?? new List<AuditEvent>();
                filtrados = delRecurso.Where(x => x.EntityRef == entityRef
                                                  && Matches(x, agentUserId, action, fromUtc, toUtc));
            }
            else
            {
                filtrados = await _repo.SearchAsync(agentUserId, action, fromUtc, toUtc, MaxFilas)
                            ?? new List<AuditEvent>();
            }

            var lista = filtrados.ToList();
            return new AuditPage
            {
                // El total es el de la búsqueda completa, no el de la página: con el de la
                // página, la última parece la única y el paginador desaparece.
                Total = lista.Count,
                Items = lista.Skip(Math.Max(0, skip)).Take(Math.Max(1, take)).ToList()
            };
        }

        /// <summary>
        /// Todos los eventos de UNA ficha (respuesta), ordenados del más reciente al más viejo.
        /// Matchea tanto el evento de creación (<c>QuestionnaireResponse/{id}</c>) como los de
        /// operación (<c>…$save</c>, <c>…$finalize</c>, <c>…$lock</c>, <c>…$retarget</c>), pero
        /// no las respuestas cuyo id sea prefijo de otra: exige el separador exacto ('$' o fin).
        /// </summary>
        public async Task<IReadOnlyList<AuditEvent>> ForResponseAsync(Guid responseId)
        {
            var prefix = "QuestionnaireResponse/" + responseId;
            var eventos = await _repo.ForEntityAsync(prefix, includeOperations: true);
            // El repositorio ya filtra; se re-verifica acá porque esta regla (separador exacto)
            // es la que define qué eventos son de la ficha, y no debe depender del proveedor SQL.
            return (eventos ?? new List<AuditEvent>())
                .Where(e => e.EntityRef == prefix
                            || (e.EntityRef != null && e.EntityRef.StartsWith(prefix + "$", StringComparison.Ordinal)))
                .OrderByDescending(e => e.Recorded)
                .ToList();
        }

        public Task<AuditEvent> GetAsync(Guid id) => _repo.GetAsync(id);

        private static bool Matches(AuditEvent e, string agentUserId, string action, DateTime? fromUtc, DateTime? toUtc)
        {
            if (!string.IsNullOrEmpty(agentUserId)
                && (e.AgentUserId == null
                    || e.AgentUserId.ToLower(CultureInfo.InvariantCulture)
                        .IndexOf(agentUserId.ToLower(CultureInfo.InvariantCulture), StringComparison.Ordinal) < 0))
                return false;
            if (!string.IsNullOrEmpty(action) && e.Action != action) return false;
            if (fromUtc.HasValue && e.Recorded < fromUtc.Value) return false;
            if (toUtc.HasValue && e.Recorded >= toUtc.Value) return false;
            return true;
        }
    }

    public class AuditPage
    {
        public int Total { get; set; }
        public IReadOnlyList<AuditEvent> Items { get; set; } = new List<AuditEvent>();
    }
}
