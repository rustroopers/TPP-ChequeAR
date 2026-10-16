using System.Net;
using System.Web.Http;
using ChequeAR.Application.Fhir;

namespace ChequeAR.Api.Controllers.Api
{
    /// <summary>Lo que cae bajo /fhir/r5 sin ruta: 404 FHIR en vez de la página de IIS.</summary>
    public class FhirNotFoundController : ApiController
    {
        [AcceptVerbs("GET", "POST", "PUT", "DELETE", "PATCH"), AllowAnonymous]
        public IHttpActionResult Handle() => Content(HttpStatusCode.NotFound, OperationOutcomes.NotFound());
    }
}
