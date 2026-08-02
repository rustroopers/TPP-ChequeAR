using System;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using ChequeAR.Api.Auth;
using ChequeAR.Application.Fhir;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Api.Controllers.Api
{
    [RoutePrefix("fhir/r5/QuestionnaireResponse")]
    public class QuestionnaireResponseController : ApiController
    {
        private readonly IQuestionnaireResponseRepository _repo;
        private readonly QuestionnaireResponseService _service;

        public QuestionnaireResponseController(IQuestionnaireResponseRepository repo, QuestionnaireResponseService service)
        {
            _repo = repo;
            _service = service;
        }

        [HttpGet, Route(""), Authorize]
        public async Task<IHttpActionResult> Search([FromUri] string subject = null, [FromUri] Guid? questionnaire = null, [FromUri] string status = null, [FromUri] int _count = 50, [FromUri] int _offset = 0)
        {
            ResponseStatus? st = null;
            if (Enum.TryParse(status, true, out ResponseStatus s)) st = s;
            var items = await _repo.SearchAsync(subject, questionnaire, st, _offset, _count);

            var recursos = items.Select(r => (Hl7.Fhir.Model.Resource)ToFhir(r));
            return Ok(FhirBundles.SearchSet(recursos, items.Count, FhirBaseUrl()));
        }

        [HttpGet, Route("{id:guid}"), Authorize]
        public async Task<IHttpActionResult> Get(Guid id)
        {
            var r = await _repo.GetAsync(id);
            if (r == null) return Content(HttpStatusCode.NotFound, OperationOutcomes.NotFound());
            return Ok(ToFhir(r));
        }

        /// <summary>
        /// Devuelve el recurso ALMACENADO, no uno regenerado: FhirJson es la representación
        /// canónica de la ficha —es lo que validó $finalize y lo que conserva los textos de la
        /// versión anclada y el paciente embebido—. Regenerarlo acá, sin la versión a mano,
        /// perdería los textos que el médico leyó.
        ///
        /// Sólo se reconstruye si el guardado está vacío o ilegible, para que una fila corrupta
        /// degrade en vez de tumbar la búsqueda entera.
        /// </summary>
        private static Hl7.Fhir.Model.QuestionnaireResponse ToFhir(QuestionnaireResponse r)
        {
            var almacenado = FhirResourceReader.ReadResponse(r.FhirJson);
            return almacenado ?? FhirQuestionnaireResponseBuilder.ToResource(r, null);
        }

        private string FhirBaseUrl()
            => Request.RequestUri.GetLeftPart(UriPartial.Authority) + "/fhir/r5";

        [HttpPost, Route(""), Authorize(Roles = Role.Administrador + "," + Role.Jefe + "," + Role.Medico)]
        public async Task<IHttpActionResult> Create([FromBody] Hl7.Fhir.Model.QuestionnaireResponse body)
        {
            var r = FhirQuestionnaireResponseParser.ToDomain(body);
            if (r == null) return Content(HttpStatusCode.BadRequest, OperationOutcomes.Invalid());

            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            try
            {
                var created = await _service.CreateAsync(r, userId);
                return Content(HttpStatusCode.Created, ToFhir(created));
            }
            catch (ArgumentException ex)
            {
                return Content(HttpStatusCode.BadRequest, OperationOutcomes.Invalid(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                return Content((HttpStatusCode)422, OperationOutcomes.Unprocessable(ex.Message));
            }
        }

        [HttpPost, Route("{id:guid}/$finalize"), Authorize(Roles = Role.Administrador + "," + Role.Jefe + "," + Role.Medico)]
        public async Task<IHttpActionResult> Finalize(Guid id)
        {
            var userId = CurrentUser.Id(User as ClaimsPrincipal);
            try
            {
                var r = await _service.FinalizeAsync(id, userId);
                return Ok(ToFhir(r));
            }
            catch (InvalidOperationException ex)
            {
                return Content((HttpStatusCode)422, OperationOutcomes.Unprocessable(ex.Message));
            }
        }
    }
}
