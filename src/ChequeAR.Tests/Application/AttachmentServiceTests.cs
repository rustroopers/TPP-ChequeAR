using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using Moq;
using Xunit;

namespace ChequeAR.Tests.Application
{
    public class AttachmentServiceTests
    {
        private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D };
        private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10 };
        private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n");

        private readonly Mock<IAttachmentStore> _store = new Mock<IAttachmentStore>();
        private readonly Mock<IQuestionnaireResponseRepository> _responses = new Mock<IQuestionnaireResponseRepository>();
        private readonly Mock<IQuestionnaireRepository> _questionnaires = new Mock<IQuestionnaireRepository>();

        private AttachmentService Sut(params string[] specialties)
            => new AttachmentService(_store.Object, _responses.Object, _questionnaires.Object, TestHelpers.GuardFor(specialties));

        // --- Tipo real por firma -----------------------------------------------------------

        [Fact]
        public void Detect_ReconoceLosTresTiposPorSuFirma()
        {
            Assert.Equal("image/png", AttachmentService.Detect(Png)?.ContentType);
            Assert.Equal("image/jpeg", AttachmentService.Detect(Jpeg)?.ContentType);
            Assert.Equal("application/pdf", AttachmentService.Detect(Pdf)?.ContentType);
        }

        [Fact]
        public void Detect_HtmlConCualquierNombre_NoEsAceptado()
        {
            Assert.Null(AttachmentService.Detect(Encoding.ASCII.GetBytes("<html><script>alert(1)</script>")));
            Assert.Null(AttachmentService.Detect(Array.Empty<byte>()));
            Assert.Null(AttachmentService.Detect(new byte[] { 0x89, 0x50 })); // firma incompleta
        }

        [Fact]
        public async Task Store_ContenidoNoPermitido_NoGuardaNada()
        {
            var result = await Sut().StoreAsync(new MemoryStream(Encoding.ASCII.GetBytes("MZ ejecutable")), "foto.png", "u1");

            Assert.Null(result);
            _store.Verify(s => s.StoreAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Store_UsaElTipoYLaExtensionReales_NoLosDelCliente()
        {
            _store.Setup(s => s.StoreAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), "u1"))
                  .ReturnsAsync(new Attachment());

            await Sut().StoreAsync(new MemoryStream(Pdf), "estudio.png", "u1");

            _store.Verify(s => s.StoreAsync(It.IsAny<Stream>(), "estudio.pdf", "application/pdf", ".pdf", "u1"), Times.Once);
        }

        [Fact]
        public async Task Store_PasadoElLimiteDiario_NoGuarda()
        {
            _store.Setup(s => s.UploadedBytesSinceAsync("u1", It.IsAny<DateTime>())).ReturnsAsync(100);
            var sut = Sut();
            sut.DailyQuotaBytes = 100 + Pdf.Length - 1;

            await Assert.ThrowsAsync<ChequeAR.Application.Exceptions.BusinessRuleException>(
                () => sut.StoreAsync(new MemoryStream(Pdf), "estudio.pdf", "u1"));
            _store.Verify(s => s.StoreAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Theory]
        [InlineData("../../web.config", ".pdf", "web.pdf")]
        [InlineData(@"C:\fakepath\informe.final.PDF", ".pdf", "informe.final.pdf")]
        [InlineData("a\"b|c<d>.png", ".png", "a_b_c_d_.png")]
        [InlineData("", ".jpg", "adjunto.jpg")]
        [InlineData(null, ".png", "adjunto.png")]
        [InlineData("radiografía.jpg", null, "radiografía.jpg")]
        public void SafeFileName_SinRutasNiCaracteresInvalidos(string original, string ext, string expected)
        {
            Assert.Equal(expected, AttachmentService.SafeFileName(original, ext));
        }

        // --- Quién puede leerlo --------------------------------------------------------------

        private void Existe(Guid id, string uploadedBy)
            => _store.Setup(s => s.GetAsync(id)).ReturnsAsync(new Attachment { Id = id, UploadedBy = uploadedBy });

        [Fact]
        public async Task GetReadable_VinculadoAFichaDeOtraEspecialidad_NoSeDevuelve()
        {
            var id = Guid.NewGuid(); var qid = Guid.NewGuid();
            Existe(id, "otro");
            _responses.Setup(r => r.FindQuestionnaireIdsByAttachmentAsync(id)).ReturnsAsync(new[] { qid });
            _questionnaires.Setup(q => q.GetSpecialtyCodeAsync(qid)).ReturnsAsync("onco");

            Assert.Null(await Sut("cardio").GetReadableAsync(id, "med.cardio"));
        }

        [Fact]
        public async Task GetReadable_VinculadoAFichaDeAlgunaDeSusEspecialidades_SeDevuelve()
        {
            var id = Guid.NewGuid(); var qid = Guid.NewGuid();
            Existe(id, "otro");
            _responses.Setup(r => r.FindQuestionnaireIdsByAttachmentAsync(id)).ReturnsAsync(new[] { qid });
            _questionnaires.Setup(q => q.GetSpecialtyCodeAsync(qid)).ReturnsAsync("neumo");

            Assert.NotNull(await Sut("cardio", "neumo").GetReadableAsync(id, "med.multi"));
        }

        [Fact]
        public async Task GetReadable_SinFichaTodavia_SoloQuienLoSubio()
        {
            var id = Guid.NewGuid();
            Existe(id, "med.cardio");
            _responses.Setup(r => r.FindQuestionnaireIdsByAttachmentAsync(id)).ReturnsAsync(Array.Empty<Guid>());

            Assert.NotNull(await Sut("cardio").GetReadableAsync(id, "med.cardio"));
            Assert.Null(await Sut("cardio").GetReadableAsync(id, "jefe.cardio"));
        }

        [Fact]
        public async Task GetReadable_Inexistente_Null()
        {
            _store.Setup(s => s.GetAsync(It.IsAny<Guid>())).ReturnsAsync((Attachment)null);

            Assert.Null(await Sut("cardio").GetReadableAsync(Guid.NewGuid(), "u"));
        }
    }
}
