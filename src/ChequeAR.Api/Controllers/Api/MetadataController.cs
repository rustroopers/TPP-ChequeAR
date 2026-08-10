using System;
using System.Linq;
using System.Web.Http;
using ChequeAR.Api.Fhir;
using Hl7.Fhir.Model;
using Hl7.Fhir.Utility;

namespace ChequeAR.Api.Controllers.Api
{
    public class MetadataController : ApiController
    {
        [HttpGet, AllowAnonymous]
        public IHttpActionResult Get()
        {
            var capability = new CapabilityStatement
            {
                Status = PublicationStatus.Active,
                Date = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                Kind = CapabilityStatementKind.Instance,
                FhirVersion = EnumUtility.ParseLiteral<FHIRVersion>(CapabilityCatalog.FhirVersion),
                Format = new[] { FhirMediaTypeFormatter.FhirJson },
                Software = new CapabilityStatement.SoftwareComponent
                {
                    Name = CapabilityCatalog.SoftwareName,
                    Version = CapabilityCatalog.SoftwareVersion
                }
            };

            var rest = new CapabilityStatement.RestComponent { Mode = CapabilityStatement.RestfulCapabilityMode.Server };

            foreach (var r in CapabilityCatalog.Resources)
            {
                var resource = new CapabilityStatement.ResourceComponent { Type = r.Type };

                foreach (var i in r.Interactions)
                {
                    var code = EnumUtility.ParseLiteral<CapabilityStatement.TypeRestfulInteraction>(i);
                    if (code.HasValue)
                        resource.Interaction.Add(new CapabilityStatement.ResourceInteractionComponent { Code = code });
                }

                foreach (var o in r.Operations)
                {
                    resource.Operation.Add(new CapabilityStatement.OperationComponent
                    {
                        Name = o.Name,
                        Definition = o.Definition
                    });
                }

                // Un recurso declarado SIN interacciones es la forma de decir "conozco este
                // tipo y no lo soporto". Es más útil para quien integra que omitirlo: la
                // omisión se lee como olvido.
                if (!string.IsNullOrWhiteSpace(r.Documentation))
                    resource.Documentation = new Markdown(r.Documentation);

                rest.Resource.Add(resource);
            }

            capability.Rest.Add(rest);
            return Ok(capability);
        }
    }
}
