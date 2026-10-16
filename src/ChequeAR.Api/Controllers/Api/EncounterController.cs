using System;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http;
using ChequeAR.Api.Fhir;
using ChequeAR.Application.Fhir;
using ChequeAR.Application.Services;

namespace ChequeAR.Api.Controllers.Api
{
    /// Encuentros como recurso del estándar, en modo SÓLO LECTURA.
    ///
    /// ChequeAR no registra episodios de atención: los deriva de las fichas que sus propios
    /// usuarios completaron. Un encuentro acá es una proyección, no un registro con vida
    /// propia — por eso sólo hay búsqueda por paciente y no lectura individual: no tendría
    /// una identidad estable que prometer.
    [RoutePrefix("fhir/r5/Encounter")]
    public class EncounterController : ApiController
    {
        private readonly PatientService _svc;
        public EncounterController(PatientService svc) { _svc = svc; }

        [HttpGet, Route(""), Authorize]
        public async Task<IHttpActionResult> Search([FromUri] string subject = null, [FromUri] int _count = 50)
        {
            if (string.IsNullOrWhiteSpace(subject))
                return Content(System.Net.HttpStatusCode.BadRequest, OperationOutcomes.Invalid(
                    "Falta el parámetro 'subject' con la historia clínica del paciente."));

            var fichas = await _svc.GetEncountersAsync(subject, FhirBundles.ClampCount(_count));
            var total = await _svc.CountEncountersAsync(subject);

            var recursos = FhirPatientProjection.ToEncounters(fichas).Cast<Hl7.Fhir.Model.Resource>();
            return Ok(FhirBundles.SearchSet(recursos, total, Request.FhirBaseUrl()));
        }

    }
}
