using System.IO;
using System.Threading.Tasks;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Application.Interfaces
{
    public interface IAttachmentStore
    {
        /// <param name="contentType">Tipo verificado por contenido (ver AttachmentService).</param>
        Task<Attachment> StoreAsync(Stream content, string originalFileName, string contentType, string extension, string uploadedBy);
        Task<Attachment> GetAsync(System.Guid attachmentId);
        Task<Stream> OpenReadAsync(System.Guid attachmentId);

        /// <summary>Bytes subidos por el usuario desde esa fecha.</summary>
        Task<long> UploadedBytesSinceAsync(string userId, System.DateTime sinceUtc);

        /// <summary>Borra archivo y fila de los adjuntos subidos antes de esa fecha que ninguna ficha usa.</summary>
        Task<int> PurgeOrphansAsync(System.DateTime uploadedBeforeUtc);
    }
}
