using System.Linq;
using System.Threading.Tasks;
using System.Web.Http;
using ChequeAR.Application.Security;
using ChequeAR.Application.Services;

namespace ChequeAR.Api.Controllers.Api
{
    /// <summary>
    /// Catálogo de especialidades (CHEQUEAR.Specialty). Sirve al select del editor y a
    /// cualquier filtro que necesite el universo completo, no sólo las especialidades que
    /// ya tienen cuestionarios (eso lo da GetSpecialtyCodesAsync).
    /// </summary>
    [RoutePrefix("api/specialties")]
    public class SpecialtyController : ApiController
    {
        private readonly SpecialtyCatalogService _catalog;
        private readonly ISpecialtyGuard _guard;

        public SpecialtyController(SpecialtyCatalogService catalog, ISpecialtyGuard guard)
        {
            _catalog = catalog;
            _guard = guard;
        }

        [HttpGet, Route(""), Authorize]
        public async Task<IHttpActionResult> List([FromUri] bool includeInactive = false)
        {
            var cat = await _catalog.GetAsync();
            var scope = _guard.Scope;

            var items = cat.Items
                .Where(s => includeInactive || s.IsActive)
                // Para no-admin, sólo las del scope: si el usuario no tiene ninguna, la
                // lista viene vacía y el editor deshabilita guardar con un mensaje claro.
                .Where(s => scope.Allows(s.Code))
                .Select(s => new
                {
                    code = s.Code,
                    name = s.Name,
                    color = cat.Color(s.Code),
                    isDefault = s.Code == cat.DefaultCode,
                    isActive = s.IsActive
                });

            return Ok(new { defaultCode = cat.DefaultCode, results = items });
        }
    }
}
