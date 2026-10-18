using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Exceptions;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Application.Services
{
    /// <summary>Consultas sobre el registro de auditoría.</summary>
    public class AuditQueryService
    {
        /// <summary>Máximo de eventos por página, pida lo que pida el cliente.</summary>
        public const int MaxPageSize = 500;

        /// <summary>
        /// Tope de la exportación. Por encima no se entrega un archivo recortado: se rechaza,
        /// porque un export de auditoría incompleto que parece completo es peor que un error.
        /// </summary>
        public const int MaxExportRows = 50000;

        private readonly IAuditRepository _repo;

        public AuditQueryService(IAuditRepository repo) { _repo = repo; }

        /// <summary>Una página de la búsqueda, con el total real de coincidencias.</summary>
        public Task<AuditPage> SearchAsync(AuditSearchCriteria criteria, int skip = 0, int take = 50)
            => _repo.SearchAsync(criteria ?? new AuditSearchCriteria(),
                                 Math.Max(0, skip), Math.Min(MaxPageSize, Math.Max(1, take)));

        /// <param name="entityRef">Recurso afectado, ej. "Questionnaire/{id}".</param>
        public Task<AuditPage> SearchAsync(
            string agentUserId = null, string action = null, string entityRef = null,
            DateTime? fromUtc = null, DateTime? toUtc = null, int skip = 0, int take = 50)
            => SearchAsync(new AuditSearchCriteria
            {
                AgentUserId = agentUserId, Action = action, EntityRef = entityRef,
                FromUtc = fromUtc, ToUtc = toUtc
            }, skip, take);

        /// <summary>Todos los eventos de la búsqueda, para exportar. Falla si superan <see cref="MaxExportRows"/>.</summary>
        public async Task<IReadOnlyList<AuditEvent>> ExportAsync(AuditSearchCriteria criteria)
        {
            var pagina = await _repo.SearchAsync(criteria ?? new AuditSearchCriteria(), 0, MaxExportRows);
            if (pagina.Total > MaxExportRows)
                throw new BusinessRuleException(
                    "La búsqueda devuelve " + pagina.Total + " eventos y el máximo para exportar es " +
                    MaxExportRows + ". Acote el período o agregue filtros.");
            return pagina.Items;
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
    }

    /// <summary>Filtros de una búsqueda de auditoría. Lo que queda en null no filtra.</summary>
    public class AuditSearchCriteria
    {
        /// <summary>Texto contenido en el usuario, sin distinguir mayúsculas.</summary>
        public string AgentUserId { get; set; }
        /// <summary>Código C/R/U/D/E.</summary>
        public string Action { get; set; }
        public DateTime? FromUtc { get; set; }
        /// <summary>Cota superior exclusiva.</summary>
        public DateTime? ToUtc { get; set; }
        /// <summary>Recurso afectado exacto, ej. "Questionnaire/{id}".</summary>
        public string EntityRef { get; set; }
        /// <summary>
        /// Todo lo que pasó con este cuestionario: sus eventos propios, incluidas las
        /// operaciones ($publish, $archive…), y los reportes consultados o exportados sobre él.
        /// </summary>
        public Guid? QuestionnaireId { get; set; }
        /// <summary>Tipo de recurso, ej. "ClinicalVariable": sus eventos y los de sus operaciones.</summary>
        public string ResourceType { get; set; }
    }

    public class AuditPage
    {
        public int Total { get; set; }
        public IReadOnlyList<AuditEvent> Items { get; set; } = new List<AuditEvent>();
    }
}
