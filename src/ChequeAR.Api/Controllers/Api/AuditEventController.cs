using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using ChequeAR.Application.Fhir;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Enums;
using Hl7.Fhir.Model;
using DomainAuditEvent = ChequeAR.Domain.Entities.AuditEvent;
using FhirAuditEvent = Hl7.Fhir.Model.AuditEvent;

namespace ChequeAR.Api.Controllers.Api
{
    [RoutePrefix("fhir/r5/AuditEvent")]
    public class AuditEventController : ApiController
    {
        private readonly IAuditRepository _repo;
        public AuditEventController(IAuditRepository repo) { _repo = repo; }

        [HttpGet, Route(""), Authorize(Roles = Role.Administrador + "," + Role.Jefe)]
        public async Task<IHttpActionResult> Search([FromUri(Name = "agent.who")] string agentWho = null, [FromUri(Name = "entity.what")] string entityWhat = null, [FromUri] int _count = 50, [FromUri] int _offset = 0)
        {
            // TODO(T087): el filtrado y la paginación se mueven a AuditQueryService en US7.
            var all = await _repo.SearchAsync(agentWho, null, null, null, 5000);
            IEnumerable<DomainAuditEvent> filtered = all;
            if (!string.IsNullOrEmpty(entityWhat)) filtered = filtered.Where(x => x.EntityRef == entityWhat);
            var list = filtered.ToList();
            var page = list.Skip(_offset).Take(_count).Select(e => (Resource)ToFhir(e));

            return Ok(FhirBundles.SearchSet(page, list.Count, FhirBaseUrl()));
        }

        // A7 — detalle de evento. Búsqueda por id directo (no FHIR-search).
        [HttpGet, Route("{id:guid}"), Authorize(Roles = Role.Administrador + "," + Role.Jefe)]
        public async Task<IHttpActionResult> Get(Guid id)
        {
            // Reusa SearchAsync con paginación amplia + filtro client-side.
            // Optimización futura: agregar IAuditRepository.GetAsync(id).
            var all = await _repo.SearchAsync(null, null, null, null, 5000);
            foreach (var e in all)
            {
                if (e.Id == id) return Ok(ToFhir(e));
            }
            return Content(HttpStatusCode.NotFound, OperationOutcomes.NotFound());
        }

        /// <summary>
        /// Proyección al recurso del estándar. El dominio guarda quién, qué, cuándo y sobre
        /// qué recurso; FHIR los ubica en agent.who, action, recorded y entity.what.
        /// </summary>
        private static FhirAuditEvent ToFhir(DomainAuditEvent e)
        {
            var recurso = new FhirAuditEvent
            {
                Id = e.Id.ToString(),
                // Lo guardado es UTC; se marca explícito para que no lo reinterprete como local.
                Recorded = new DateTimeOffset(DateTime.SpecifyKind(e.Recorded, DateTimeKind.Utc)),
                Action = ParseAction(e.Action)
            };

            if (!string.IsNullOrWhiteSpace(e.AgentUserId))
            {
                recurso.Agent.Add(new FhirAuditEvent.AgentComponent
                {
                    Who = new ResourceReference { Display = e.AgentUserId }
                });
            }

            if (!string.IsNullOrWhiteSpace(e.EntityRef))
            {
                recurso.Entity.Add(new FhirAuditEvent.EntityComponent
                {
                    What = new ResourceReference { Reference = e.EntityRef }
                });
            }

            return recurso;
        }

        /// <summary>
        /// El dominio ya guarda la acción con el vocabulario del estándar (C/R/U/D/E). Lo que
        /// no matchee queda sin action antes que inventar un valor: un dato ausente es honesto,
        /// uno equivocado no — y en auditoría eso importa más que en cualquier otro lado.
        /// </summary>
        private static FhirAuditEvent.AuditEventAction? ParseAction(string action)
        {
            if (string.IsNullOrWhiteSpace(action)) return null;
            return Hl7.Fhir.Utility.EnumUtility.ParseLiteral<FhirAuditEvent.AuditEventAction>(
                action.Trim().ToUpperInvariant());
        }

        private string FhirBaseUrl()
            => Request.RequestUri.GetLeftPart(UriPartial.Authority) + "/fhir/r5";
    }
}
