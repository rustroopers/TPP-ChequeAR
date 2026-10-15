using System;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using ChequeAR.Api.Auth;
using ChequeAR.Application.Services;

namespace ChequeAR.Api.Controllers.Api
{
    [RoutePrefix("api/attachments")]
    public class AttachmentController : ApiController
    {
        private readonly AttachmentService _attachments;
        public AttachmentController(AttachmentService attachments) { _attachments = attachments; }

        /// <summary>Jpeg, png o pdf; el tipo se verifica por el contenido, no por el nombre.</summary>
        [HttpPost, Route(""), Authorize]
        public async Task<IHttpActionResult> Upload()
        {
            if (!Request.Content.IsMimeMultipartContent())
                return StatusCode(HttpStatusCode.UnsupportedMediaType);

            var maxBytes = long.Parse(ConfigurationManager.AppSettings["Attachments:MaxBytes"] ?? "10485760");
            var provider = await Request.Content.ReadAsMultipartAsync(new MultipartMemoryStreamProvider());
            var part = provider.Contents.FirstOrDefault();
            if (part == null) return BadRequest("no file");

            using (var stream = await part.ReadAsStreamAsync())
            {
                if (stream.Length > maxBytes) return Content((HttpStatusCode)413, new { error = "file too large" });
                var userId = CurrentUser.Id(User as ClaimsPrincipal);
                var fileName = part.Headers.ContentDisposition?.FileName?.Trim('"');
                var attachment = await _attachments.StoreAsync(stream, fileName, userId);
                if (attachment == null)
                    return Content(HttpStatusCode.UnsupportedMediaType,
                        new { error = "Solo se aceptan imágenes JPG o PNG y documentos PDF." });

                return Content(HttpStatusCode.Created, new
                {
                    id = attachment.Id,
                    contentType = attachment.ContentType,
                    size = attachment.SizeBytes,
                    hash = attachment.Hash
                });
            }
        }

        /// <summary>
        /// 404 si no puede ver la ficha que lo usa. Va como "attachment": el front lo abre desde un blob.
        /// </summary>
        [HttpGet, Route("{id:guid}"), Authorize]
        public async Task<HttpResponseMessage> Download(Guid id)
        {
            var meta = await _attachments.GetReadableAsync(id, CurrentUser.Id(User as ClaimsPrincipal));
            if (meta == null) return Request.CreateResponse(HttpStatusCode.NotFound);

            var stream = await _attachments.OpenReadAsync(id);
            if (stream == null) return Request.CreateResponse(HttpStatusCode.NotFound);

            var resp = Request.CreateResponse(HttpStatusCode.OK);
            resp.Content = new StreamContent(stream);
            resp.Content.Headers.ContentType = new MediaTypeHeaderValue(meta.ContentType ?? "application/octet-stream");
            resp.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                FileName = AttachmentService.SafeFileName(meta.OriginalFileName, null)
            };
            return resp;
        }
    }
}
