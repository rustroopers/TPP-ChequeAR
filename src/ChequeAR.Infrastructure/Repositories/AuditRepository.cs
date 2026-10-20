using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using ChequeAR.Infrastructure.EF;

namespace ChequeAR.Infrastructure.Repositories
{
    /// <summary>
    /// Se usa de dos maneras (ver ServiceComposition): con el contexto del request, que libera
    /// el contenedor, o con uno propio por operación, que es como lo usa AuditDispatcher (un
    /// singleton que no puede retener un DbContext de por vida). <c>ownsContext</c> dice cuál.
    /// </summary>
    public sealed class AuditRepository : IAuditRepository, IDisposable
    {
        private readonly ChequeARDbContext _ctx;
        private readonly bool _ownsContext;

        public AuditRepository(ChequeARDbContext ctx, bool ownsContext)
        {
            _ctx = ctx;
            _ownsContext = ownsContext;
        }

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

        public async Task<AuditPage> SearchAsync(AuditSearchCriteria criteria, int skip, int take)
        {
            var agentUserId = criteria.AgentUserId;
            var action = criteria.Action;
            var fromUtc = criteria.FromUtc;
            var toUtc = criteria.ToUtc;
            var entityRef = criteria.EntityRef;

            IQueryable<AuditEvent> q = _ctx.AuditEvents.AsNoTracking();
            if (!string.IsNullOrEmpty(agentUserId))
            {
                var u = agentUserId.ToLower(CultureInfo.InvariantCulture);
                // x.AgentUserId.ToLower() queda tal cual (sin Invariant): esto es una expresion
                // LINQ-to-Entities que el proveedor de Oracle traduce a LOWER() en SQL; el
                // overload con CultureInfo no es un metodo que EF6 sepa traducir.
#pragma warning disable CA1304, CA1311, CA1862
                q = q.Where(x => x.AgentUserId.ToLower().StartsWith(u));
#pragma warning restore CA1304, CA1311, CA1862
            }
            if (!string.IsNullOrEmpty(action)) q = q.Where(x => x.Action == action);
            if (fromUtc.HasValue) q = q.Where(x => x.Recorded >= fromUtc.Value);
            if (toUtc.HasValue) q = q.Where(x => x.Recorded < toUtc.Value);
            if (!string.IsNullOrEmpty(entityRef)) q = q.Where(x => x.EntityRef == entityRef);
            if (criteria.QuestionnaireId.HasValue)
            {
                // Eventos del cuestionario ("Questionnaire/{id}" + sufijo de operación) y de los
                // reportes que corrieron sobre él ("Report/…?questionnaire={id}&…").
                var propio = "Questionnaire/" + criteria.QuestionnaireId.Value;
                var enReporte = "questionnaire=" + criteria.QuestionnaireId.Value;
                q = q.Where(x => x.EntityRef.StartsWith(propio)
                              || (x.EntityType == "Report" && x.EntityRef.Contains(enReporte)));
            }
            if (!string.IsNullOrEmpty(criteria.ResourceType))
            {
                // Por la referencia y no por EntityType: "Patient$search" no tiene barra y
                // queda sin tipo. La barra o el '$' evitan que "Questionnaire" incluya a
                // "QuestionnaireResponse".
                var conId = criteria.ResourceType + "/";
                var conOperacion = criteria.ResourceType + "$";
                q = q.Where(x => x.EntityRef.StartsWith(conId) || x.EntityRef.StartsWith(conOperacion));
            }

            var total = await q.CountAsync();
            // El desempate por Id hace estable la paginación entre eventos del mismo instante.
            // FhirJson sólo viaja para las consultas guardadas (ver IAuditRepository).
            var rows = await q
                .OrderByDescending(x => x.Recorded).ThenBy(x => x.Id)
                .Skip(() => skip).Take(() => take)
                .Select(x => new
                {
                    x.Id, x.Recorded, x.Action, x.Outcome, x.AgentUserId, x.EntityRef, x.EntityType, x.Category,
                    FhirJson = x.EntityType == "SavedReport" ? x.FhirJson : null
                })
                .ToListAsync();

            return new AuditPage
            {
                Total = total,
                Items = rows.Select(r => new AuditEvent
                {
                    Id = r.Id, Recorded = r.Recorded, Action = r.Action, Outcome = r.Outcome,
                    AgentUserId = r.AgentUserId, EntityRef = r.EntityRef, EntityType = r.EntityType,
                    Category = r.Category, FhirJson = r.FhirJson
                }).ToList()
            };
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

        public void Dispose()
        {
            if (_ownsContext) _ctx.Dispose();
        }
    }
}
