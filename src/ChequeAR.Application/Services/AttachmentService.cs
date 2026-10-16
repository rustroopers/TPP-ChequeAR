using System;
using System.IO;
using System.Threading.Tasks;
using ChequeAR.Application.Exceptions;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Security;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Application.Services
{
    /// <summary>
    /// El tipo se decide por la firma del contenido, no por lo que declara el cliente. Un
    /// adjunto vinculado se lee con el scope de sus fichas; sin vincular, solo quien lo subió.
    /// </summary>
    public class AttachmentService
    {
        private static readonly (string ContentType, string Extension, byte[] Signature)[] Known =
        {
            ("image/jpeg", ".jpg", new byte[] { 0xFF, 0xD8, 0xFF }),
            ("image/png", ".png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            ("application/pdf", ".pdf", new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }) // "%PDF-"
        };

        public const long DefaultDailyQuotaBytes = 200L * 1024 * 1024;

        /// <summary>Lo que un usuario puede subir en 24 horas: el disco del plan F1 es de 1 GB.</summary>
        public long DailyQuotaBytes { get; set; } = DefaultDailyQuotaBytes;

        private readonly IAttachmentStore _store;
        private readonly IQuestionnaireResponseRepository _responses;
        private readonly IQuestionnaireRepository _questionnaires;
        private readonly ISpecialtyGuard _guard;

        public AttachmentService(IAttachmentStore store, IQuestionnaireResponseRepository responses,
            IQuestionnaireRepository questionnaires, ISpecialtyGuard guard)
        {
            _store = store;
            _responses = responses;
            _questionnaires = questionnaires;
            _guard = guard;
        }

        /// <summary>Tipo real según la firma del contenido, o null si no es uno de los aceptados.</summary>
        public static (string ContentType, string Extension)? Detect(byte[] header)
        {
            if (header == null) return null;
            foreach (var k in Known)
            {
                if (header.Length < k.Signature.Length) continue;
                var match = true;
                for (var i = 0; i < k.Signature.Length && match; i++)
                    match = header[i] == k.Signature[i];
                if (match) return (k.ContentType, k.Extension);
            }
            return null;
        }

        /// <summary>Null si el tipo no es aceptado. <paramref name="content"/> tiene que poder posicionarse.</summary>
        public async Task<Attachment> StoreAsync(Stream content, string originalFileName, string uploadedBy)
        {
            var header = new byte[16];
            content.Position = 0;
            var read = await content.ReadAsync(header, 0, header.Length);
            Array.Resize(ref header, read);
            content.Position = 0;

            var tipo = Detect(header);
            if (tipo == null) return null;

            var subido = await _store.UploadedBytesSinceAsync(uploadedBy, DateTime.UtcNow.AddHours(-24));
            if (subido + content.Length > DailyQuotaBytes)
                throw new BusinessRuleException("Se alcanzó el límite de adjuntos de las últimas 24 horas ("
                    + (DailyQuotaBytes / (1024 * 1024)) + " MB).");

            return await _store.StoreAsync(content, SafeFileName(originalFileName, tipo.Value.Extension),
                tipo.Value.ContentType, tipo.Value.Extension, uploadedBy);
        }

        /// <summary>Null si no existe o no lo puede leer.</summary>
        public async Task<Attachment> GetReadableAsync(Guid attachmentId, string userId)
        {
            var meta = await _store.GetAsync(attachmentId);
            if (meta == null) return null;

            var questionnaireIds = await _responses.FindQuestionnaireIdsByAttachmentAsync(attachmentId);
            if (questionnaireIds.Count == 0)
                return string.Equals(meta.UploadedBy, userId, StringComparison.OrdinalIgnoreCase) ? meta : null;

            // alcanza con una: solo quien lo subió puede vincularlo (QuestionnaireResponseService)
            foreach (var qid in questionnaireIds)
            {
                var specialty = await _questionnaires.GetSpecialtyCodeAsync(qid);
                if (_guard.Scope.Allows(specialty)) return meta;
            }
            return null;
        }

        public Task<Stream> OpenReadAsync(Guid attachmentId) => _store.OpenReadAsync(attachmentId);

        /// <summary>Sin rutas ni caracteres inválidos; <paramref name="extension"/> null conserva la del nombre.</summary>
        public static string SafeFileName(string original, string extension)
        {
            var name = original ?? string.Empty;
            var slash = Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\'));
            if (slash >= 0) name = name.Substring(slash + 1);

            var invalid = Path.GetInvalidFileNameChars();
            var chars = name.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
                if (Array.IndexOf(invalid, chars[i]) >= 0 || chars[i] == '"' || char.IsControl(chars[i])) chars[i] = '_';
            name = new string(chars).Trim().Trim('.');

            var dot = name.LastIndexOf('.');
            var stem = dot > 0 ? name.Substring(0, dot) : name;
            var ext = extension ?? (dot > 0 ? name.Substring(dot) : string.Empty);
            if (string.IsNullOrWhiteSpace(stem)) stem = "adjunto";
            if (stem.Length > 120) stem = stem.Substring(0, 120);
            return stem + ext;
        }
    }
}
