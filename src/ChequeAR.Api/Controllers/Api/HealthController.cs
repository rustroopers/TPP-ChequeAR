using System;
using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using ChequeAR.Application.Interfaces;

namespace ChequeAR.Api.Controllers.Api
{
    /// <summary>
    /// Para el smoke del deploy: la primera consulta aplica las migraciones pendientes, así que
    /// si fallan, el deploy se entera acá y no con el primer usuario.
    /// </summary>
    [RoutePrefix("api/health")]
    public class HealthController : ApiController
    {
        private readonly ISpecialtyRepository _specialties;

        public HealthController(ISpecialtyRepository specialties)
        {
            _specialties = specialties;
        }

        [HttpGet, Route(""), AllowAnonymous]
        public async Task<IHttpActionResult> Get()
        {
            try
            {
                await _specialties.ListAsync();
                return Ok(new { status = "ok" });
            }
            catch (Exception ex)
            {
                Trace.TraceError("Health: la base no responde: " + ex);
                return Content(HttpStatusCode.ServiceUnavailable, new { status = "down" });
            }
        }
    }
}
