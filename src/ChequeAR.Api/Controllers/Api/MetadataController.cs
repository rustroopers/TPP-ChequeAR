using System;
using System.Web.Http;

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
                software = new { name = "ChequeAR", version = "0.1.0" },
                fhirVersion = "5.0.0",
                format = new[] { "application/fhir+json" },
                rest = new[]
                {
                    new
                    {
                        mode = "server",
                        resource = new object[]
                        {
                            new { type = "Questionnaire", interaction = new[] { new { code = "read" }, new { code = "search-type" }, new { code = "create" }, new { code = "update" }, new { code = "delete" } }, operation = new[] { new { name = "publish", definition = "https://chequear.garrahan/OperationDefinition/publish" }, new { name = "archive", definition = "https://chequear.garrahan/OperationDefinition/archive" } } },
                            new { type = "QuestionnaireResponse", interaction = new[] { new { code = "read" }, new { code = "search-type" }, new { code = "create" }, new { code = "update" } }, operation = new[] { new { name = "finalize", definition = "https://chequear.garrahan/OperationDefinition/finalize" } } },
                            new { type = "AuditEvent", interaction = new[] { new { code = "read" }, new { code = "search-type" } }, operation = new object[0] }
                        }
                    }
                }
            };
            return Ok(capability);
        }
    }
}
