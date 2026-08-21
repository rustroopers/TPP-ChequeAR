using System;
using System.IO;
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

        public async Task<Attachment> StoreAsync(Stream content, string originalFileName, string contentType, string uploadedBy)
        {
            var id = Guid.NewGuid();
            var subdir = Path.Combine(_root, DateTime.UtcNow.ToString("yyyy"), DateTime.UtcNow.ToString("MM"));
            Directory.CreateDirectory(subdir);

            var safeName = id.ToString() + Path.GetExtension(originalFileName ?? string.Empty);
            var fullPath = Path.Combine(subdir, safeName);

            string hash;
            long size;
            using (var sha = SHA256.Create())
            using (var fs = File.Create(fullPath))
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
                StoragePath = fullPath,
                UploadedBy = uploadedBy,
                UploadedAt = DateTime.UtcNow,
                Hash = hash
            };

            // Sin esto no había forma de resolver de vuelta path/content-type a partir del id:
            // el archivo quedaba en disco pero "huérfano" para cualquier request posterior.
            _ctx.Attachments.Add(attachment);
            await _ctx.SaveChangesAsync();

            return attachment;
        }

        public Task<Attachment> GetAsync(Guid attachmentId) => _ctx.Attachments.FindAsync(attachmentId);

        public async Task<Stream> OpenReadAsync(Guid attachmentId)
        {
            var attachment = await _ctx.Attachments.FindAsync(attachmentId);
            if (attachment == null || !File.Exists(attachment.StoragePath)) return null;
            return File.OpenRead(attachment.StoragePath);
        }
    }
}
