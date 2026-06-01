using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Infrastructure.Storage
{
    public class FileShareAttachmentStore : IAttachmentStore
    {
        private readonly string _root;

        public FileShareAttachmentStore(string rootPath)
        {
            _root = rootPath;
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

            return new Attachment
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
        }

        public Task<Stream> OpenReadAsync(Guid attachmentId)
        {
            throw new NotImplementedException("Resolver path desde repo y abrir el archivo (no implementado en MVP).");
        }
    }
}
