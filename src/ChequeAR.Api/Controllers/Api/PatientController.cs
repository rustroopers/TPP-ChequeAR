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
        public async Task<IHttpActionResult> Search([FromUri] string name = null, [FromUri] string q = null,
            [FromUri] int _count = PatientService.MaxSearchResults, [FromUri] int _offset = 0)
        {
            // 'name' es el parámetro de búsqueda del estándar; 'q' se acepta por
            // compatibilidad con lo que usaban las pantallas antes de la feature 009.
            var texto = !string.IsNullOrWhiteSpace(name) ? name : q;
            var count = FhirBundles.ClampCount(_count);
            var offset = Math.Min(FhirBundles.ClampOffset(_offset), PatientService.MaxSearchWindow - 1);
            // Uno de más para saber si hay otra página: la vista no da un total.
            var window = Math.Min(offset + count + 1, PatientService.MaxSearchWindow);
            var hits = await _svc.SearchAsync(texto, window);

            var pagina = hits.Skip(offset).Take(count).ToList();
            var recursos = FhirPatientProjection.ToPatients(pagina).Cast<Hl7.Fhir.Model.Resource>();
            var total = hits.Count < window ? hits.Count : (int?)null;
            return Ok(FhirBundles.SearchSet(recursos, total, Request.FhirBaseUrl(), Request.RequestUri, offset, count,
                hasMore: hits.Count > offset + count));
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
