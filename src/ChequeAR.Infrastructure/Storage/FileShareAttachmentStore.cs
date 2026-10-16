using System;
using System.Globalization;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Entities;
using ChequeAR.Infrastructure.EF;

namespace ChequeAR.Infrastructure.Storage
{
    public class FileShareAttachmentStore : IAttachmentStore
    {
        private readonly string _root;
        private readonly ChequeARDbContext _ctx;

        public FileShareAttachmentStore(string rootPath, ChequeARDbContext ctx)
        {
            _root = rootPath;
            _ctx = ctx;
            if (!Directory.Exists(_root)) Directory.CreateDirectory(_root);
        }

        public async Task<Attachment> StoreAsync(Stream content, string originalFileName, string contentType, string extension, string uploadedBy)
        {
            var id = Guid.NewGuid();
            var now = DateTime.UtcNow;
            // relativa, para que sobreviva a un cambio de Attachments:Root
            var relativeDir = Path.Combine(now.ToString("yyyy", CultureInfo.InvariantCulture), now.ToString("MM", CultureInfo.InvariantCulture));
            var relativePath = Path.Combine(relativeDir, id.ToString("D") + extension);
            var fullPath = Path.Combine(_root, relativePath);
            Directory.CreateDirectory(Path.Combine(_root, relativeDir));

            // temporal hasta que el INSERT funcione, para no dejar archivos huérfanos
            var tempPath = fullPath + ".tmp";
            string hash;
            long size;
            try
            {
                using (var sha = SHA256.Create())
                using (var fs = File.Create(tempPath))
                {
                    await content.CopyToAsync(fs);
                    fs.Position = 0;
                    hash = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
                    size = fs.Length;
                }

                var attachment = new Attachment
                {
                    Id = id,
                    OriginalFileName = originalFileName,
                    ContentType = contentType,
                    SizeBytes = size,
                    StoragePath = relativePath,
                    UploadedBy = uploadedBy,
                    UploadedAt = now,
                    Hash = hash
                };

                // Sin esto no había forma de resolver de vuelta path/content-type a partir del id:
                // el archivo quedaba en disco pero "huérfano" para cualquier request posterior.
                _ctx.Attachments.Add(attachment);
                await _ctx.SaveChangesAsync();

                File.Move(tempPath, fullPath);
                return attachment;
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        public Task<Attachment> GetAsync(Guid attachmentId) => _ctx.Attachments.FindAsync(attachmentId);

        public async Task<Stream> OpenReadAsync(Guid attachmentId)
        {
            var attachment = await _ctx.Attachments.FindAsync(attachmentId);
            if (attachment == null) return null;
            var path = FullPath(attachment);
            return File.Exists(path) ? File.OpenRead(path) : null;
        }

        public async Task<int> PurgeOrphansAsync(DateTime uploadedBeforeUtc)
        {
            var huerfanos = await _ctx.Attachments
                .Where(a => a.UploadedAt < uploadedBeforeUtc
                            && !_ctx.QuestionnaireResponseItems.Any(i => i.AttachmentId == a.Id))
                .OrderBy(a => a.UploadedAt)
                .Take(500)
                .ToListAsync();
            foreach (var a in huerfanos)
            {
                // Primero la fila: si falla el borrado del archivo queda basura en disco, no
                // una fila que apunta a nada.
                _ctx.Attachments.Remove(a);
            }
            await _ctx.SaveChangesAsync();
            foreach (var a in huerfanos)
            {
                var path = FullPath(a);
                try { if (File.Exists(path)) File.Delete(path); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return huerfanos.Count;
        }

        // los adjuntos viejos guardaron la ruta absoluta
        private string FullPath(Attachment attachment) => Path.IsPathRooted(attachment.StoragePath)
            ? attachment.StoragePath
            : Path.Combine(_root, attachment.StoragePath);
    }
}
