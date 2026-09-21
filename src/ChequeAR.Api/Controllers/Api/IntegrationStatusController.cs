using System.Threading.Tasks;
using System.Web.Http;
using ChequeAR.Api.Auth;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Api.Controllers.Api
{
    /// Endpoint de salud de integraciones externas (F1).
    [RoutePrefix("api/integrations")]
    public class IntegrationStatusController : ApiController
    {
        private readonly IntegrationHealthService _svc;
        public IntegrationStatusController(IntegrationHealthService svc) { _svc = svc; }

        [HttpGet, Route("status"), RequiresPermissionApi(Permissions.IntegrationRead)]
        public async Task<IHttpActionResult> Status()
        {
            var probes = await _svc.ProbeAllAsync();
            return Ok(new { integrations = probes });
        }
    }
}
