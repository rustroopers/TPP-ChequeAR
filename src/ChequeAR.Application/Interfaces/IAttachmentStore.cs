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
    }
}
