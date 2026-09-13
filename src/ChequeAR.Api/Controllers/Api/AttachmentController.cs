using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using ChequeAR.Api.Auth;
using ChequeAR.Application.Interfaces;

namespace ChequeAR.Api.Controllers.Api
{
    [RoutePrefix("api/attachments")]
    public class AttachmentController : ApiController
    {
        private static readonly HashSet<string> AllowedContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg", "image/png", "application/pdf"
        };

        private readonly IAttachmentStore _store;
        public AttachmentController(IAttachmentStore store) { _store = store; }

        [HttpPost, Route(""), Authorize]
        public async Task<IHttpActionResult> Upload()
        {
            if (!Request.Content.IsMimeMultipartContent())
                return StatusCode(HttpStatusCode.UnsupportedMediaType);

            var maxBytes = long.Parse(ConfigurationManager.AppSettings["Attachments:MaxBytes"] ?? "10485760");
            var provider = await Request.Content.ReadAsMultipartAsync(new MultipartMemoryStreamProvider());
            var part = provider.Contents.FirstOrDefault();
            if (part == null) return BadRequest("no file");

            var contentType = part.Headers.ContentType?.MediaType;
            if (!AllowedContentTypes.Contains(contentType ?? ""))
                return StatusCode(HttpStatusCode.UnsupportedMediaType);

            using (var stream = await part.ReadAsStreamAsync())
            {
                if (stream.Length > maxBytes) return Content((HttpStatusCode)413, new { error = "file too large" });
                stream.Position = 0;
                var userId = CurrentUser.Id(User as ClaimsPrincipal);
                var fileName = part.Headers.ContentDisposition?.FileName?.Trim('"');
                var attachment = await _store.StoreAsync(stream, fileName, contentType, userId);
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
        /// Devuelve el contenido de un adjunto ya subido. "inline" (no "attachment") a
        /// propósito: los tres tipos permitidos (jpeg/png/pdf) los renderiza el navegador
        /// solo, así que abre en una pestaña en vez de forzar la descarga.
        /// </summary>
        [HttpGet, Route("{id:guid}"), Authorize]
        public async Task<HttpResponseMessage> Download(Guid id)
        {
            var meta = await _store.GetAsync(id);
            if (meta == null) return Request.CreateResponse(HttpStatusCode.NotFound);

            var stream = await _store.OpenReadAsync(id);
            if (stream == null) return Request.CreateResponse(HttpStatusCode.NotFound);

            var resp = Request.CreateResponse(HttpStatusCode.OK);
            resp.Content = new StreamContent(stream);
            resp.Content.Headers.ContentType = new MediaTypeHeaderValue(meta.ContentType ?? "application/octet-stream");
            resp.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline") { FileName = meta.OriginalFileName };
            return resp;
        }
    }
}
