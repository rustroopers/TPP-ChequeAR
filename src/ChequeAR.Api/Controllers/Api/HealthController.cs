using System;
using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using ChequeAR.Application.Interfaces;

namespace ChequeAR.Api.Controllers.Api
{
    /// <summary>
    /// Para el smoke del deploy: que la base responda y, en producción, que no hayan quedado
    /// migraciones sin aplicar.
    /// </summary>
    [RoutePrefix("api/health")]
    public class HealthController : ApiController
    {
        private static bool AutomaticMigrations => !string.Equals(
            System.Configuration.ConfigurationManager.AppSettings["Migrations:Automatic"], "false", StringComparison.OrdinalIgnoreCase);

        private readonly ISpecialtyRepository _specialties;
        private readonly ISchemaMigrator _migrator;

        public HealthController(ISpecialtyRepository specialties, ISchemaMigrator migrator)
        {
            _specialties = specialties;
            _migrator = migrator;
        }

        [HttpGet, Route(""), AllowAnonymous]
        public async Task<IHttpActionResult> Get()
        {
            try
            {
                await _specialties.ListAsync();
                // En producción las migraciones las aplica el deploy: si quedaron pendientes,
                // el smoke tiene que fallar.
                if (!AutomaticMigrations && _migrator.Pending().Count > 0)
                    return Content(HttpStatusCode.ServiceUnavailable, new { status = "migraciones pendientes" });
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
