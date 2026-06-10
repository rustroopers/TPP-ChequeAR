using System.Threading.Tasks;
using System.Web.Http;
using ChequeAR.Application.Services;

namespace ChequeAR.Api.Controllers.Api
{
    /// Endpoints de pacientes para R1 (buscador), R2 (selector) y R7 (timeline).
    /// ChequeAR no posee Patient — proxy a Hospital FHIR + Oracle vistas.
    [RoutePrefix("api/patients")]
    public class PatientController : ApiController
    {
        private readonly PatientService _svc;
        public PatientController(PatientService svc) { _svc = svc; }

        [HttpGet, Route(""), Authorize]
        public async Task<IHttpActionResult> Search([FromUri] string q = null)
        {
            var hits = await _svc.SearchAsync(q);
            return Ok(new { total = hits.Count, results = hits });
        }

        [HttpGet, Route("{hospitalId}"), Authorize]
        public async Task<IHttpActionResult> Get(string hospitalId)
        {
            var p = await _svc.GetAsync(hospitalId);
            if (p == null) return NotFound();
            return Ok(p);
        }

        [HttpGet, Route("{hospitalId}/encounters"), Authorize]
        public async Task<IHttpActionResult> Encounters(string hospitalId, [FromUri] int _count = 50)
        {
            var enc = await _svc.GetEncountersAsync(hospitalId, _count);
            return Ok(new { total = enc.Count, entry = enc });
        }
    }
}
