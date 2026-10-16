using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using ChequeAR.Api.Fhir;
using ChequeAR.Application.Fhir;
using ChequeAR.Application.Services;

namespace ChequeAR.Api.Controllers.Api
{
    /// Pacientes como recurso del estándar, en modo SÓLO LECTURA.
    ///
    /// ChequeAR no posee la entidad Patient: la deriva de la vista del hospital y, cuando esa
    /// fuente no responde, de sus propias fichas. Por eso no hay create ni update — exponer
    /// escritura sobre datos que no son nuestros sería prometer algo que no podemos cumplir.
    [RoutePrefix("fhir/r5/Patient")]
    public class PatientController : ApiController
    {
        private readonly PatientService _svc;
        public PatientController(PatientService svc) { _svc = svc; }

        [HttpGet, Route(""), Authorize]
        public async Task<IHttpActionResult> Search([FromUri] string name = null, [FromUri] string q = null)
        {
            // 'name' es el parámetro de búsqueda del estándar; 'q' se acepta por
            // compatibilidad con lo que usaban las pantallas antes de la feature 009.
            var texto = !string.IsNullOrWhiteSpace(name) ? name : q;
            var hits = await _svc.SearchAsync(texto);

            var recursos = FhirPatientProjection.ToPatients(hits).Cast<Hl7.Fhir.Model.Resource>();
            // Con el tope alcanzado puede haber más coincidencias: el total no se conoce.
            var total = hits.Count < PatientService.MaxSearchResults ? hits.Count : (int?)null;
            return Ok(FhirBundles.SearchSet(recursos, total, Request.FhirBaseUrl()));
        }

        [HttpGet, Route("{hospitalId}"), Authorize]
        public async Task<IHttpActionResult> Get(string hospitalId)
        {
            var p = await _svc.GetAsync(hospitalId);
            if (p == null) return Content(HttpStatusCode.NotFound, OperationOutcomes.NotFound());
            return Ok(FhirPatientProjection.ToPatient(p));
        }

    }
}
