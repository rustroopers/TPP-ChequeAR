using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using ChequeAR.Api.Fhir;
using ChequeAR.Api.Auth;
using ChequeAR.Application.Fhir;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Enums;
using Hl7.Fhir.Model;

namespace ChequeAR.Api.Controllers.Api
{
    [RoutePrefix("fhir/r5/AuditEvent")]
    public class AuditEventController : ApiController
    {
        private readonly AuditQueryService _audit;
        public AuditEventController(AuditQueryService audit) { _audit = audit; }

        [HttpGet, Route(""), RequiresPermissionApi(Permissions.AuditRead)]
        public async Task<IHttpActionResult> Search([FromUri(Name = "agent.who")] string agentWho = null, [FromUri(Name = "entity.what")] string entityWhat = null, [FromUri] int _count = 50, [FromUri] int _offset = 0)
        {
            var pagina = await _audit.SearchAsync(agentWho, entityRef: entityWhat,
                skip: FhirBundles.ClampOffset(_offset), take: FhirBundles.ClampCount(_count));

            var recursos = pagina.Items.Select(e => (Resource)FhirAuditEventBuilder.Build(e));
            return Ok(FhirBundles.SearchSet(recursos, pagina.Total, Request.FhirBaseUrl(), Request.RequestUri,
                FhirBundles.ClampOffset(_offset), FhirBundles.ClampCount(_count)));
        }

        // A7 — detalle de evento. Búsqueda por id directo (no FHIR-search).
        [HttpGet, Route("{id:guid}"), RequiresPermissionApi(Permissions.AuditRead)]
        public async Task<IHttpActionResult> Get(Guid id)
        {
            var evento = await _audit.GetAsync(id);
            if (evento == null) return Content(HttpStatusCode.NotFound, OperationOutcomes.NotFound());
            return Ok(FhirAuditEventBuilder.Build(evento));
        }

    }
}
