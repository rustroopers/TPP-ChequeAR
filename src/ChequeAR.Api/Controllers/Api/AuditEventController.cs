using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using ChequeAR.Application.Fhir;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Enums;
using Hl7.Fhir.Model;
using DomainAuditEvent = ChequeAR.Domain.Entities.AuditEvent;
using FhirAuditEvent = Hl7.Fhir.Model.AuditEvent;

namespace ChequeAR.Api.Controllers.Api
{
    [RoutePrefix("fhir/r5/AuditEvent")]
    public class AuditEventController : ApiController
    {
        private readonly AuditQueryService _audit;
        public AuditEventController(AuditQueryService audit) { _audit = audit; }

        [HttpGet, Route(""), Authorize(Roles = Role.Administrador + "," + Role.Jefe)]
        public async Task<IHttpActionResult> Search([FromUri(Name = "agent.who")] string agentWho = null, [FromUri(Name = "entity.what")] string entityWhat = null, [FromUri] int _count = 50, [FromUri] int _offset = 0)
        {
            var pagina = await _audit.SearchAsync(agentWho, entityRef: entityWhat, skip: _offset, take: _count);

            var recursos = pagina.Items.Select(e => (Resource)ToFhir(e));
            return Ok(FhirBundles.SearchSet(recursos, pagina.Total, FhirBaseUrl()));
        }

        // A7 — detalle de evento. Búsqueda por id directo (no FHIR-search).
        [HttpGet, Route("{id:guid}"), Authorize(Roles = Role.Administrador + "," + Role.Jefe)]
        public async Task<IHttpActionResult> Get(Guid id)
        {
            var evento = await _audit.GetAsync(id);
            if (evento == null) return Content(HttpStatusCode.NotFound, OperationOutcomes.NotFound());
            return Ok(ToFhir(evento));
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
