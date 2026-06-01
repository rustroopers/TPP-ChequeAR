using System.IO;
using System.Threading.Tasks;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Application.Interfaces
{
    public interface IAttachmentStore
    {
        Task<Attachment> StoreAsync(Stream content, string originalFileName, string contentType, string uploadedBy);
        Task<Stream> OpenReadAsync(System.Guid attachmentId);
    }
}
