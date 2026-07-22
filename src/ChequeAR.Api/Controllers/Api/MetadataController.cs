using System;
using System.Linq;
using System.Web.Http;
using ChequeAR.Api.Fhir;

namespace ChequeAR.Api.Controllers.Api
{
    public class MetadataController : ApiController
    {
        [HttpGet, AllowAnonymous]
        public IHttpActionResult Get()
        {
            var capability = new
            {
                resourceType = "CapabilityStatement",
                status = "active",
                date = DateTime.UtcNow.ToString("o"),
                kind = "instance",
                software = new { name = CapabilityCatalog.SoftwareName, version = CapabilityCatalog.SoftwareVersion },
                fhirVersion = CapabilityCatalog.FhirVersion,
                format = new[] { "application/fhir+json" },
                rest = new[]
                {
                    new
                    {
                        mode = "server",
                        resource = CapabilityCatalog.Resources.Select(r => new
                        {
                            type = r.Type,
                            interaction = r.Interactions.Select(i => new { code = i }).ToArray(),
                            operation = r.Operations.Select(o => new { name = o.Name, definition = o.Definition }).ToArray()
                        }).ToArray()
                    }
                }
            };
            return Ok(capability);
        }
    }
}
