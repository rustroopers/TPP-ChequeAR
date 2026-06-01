using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
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
    }
}
