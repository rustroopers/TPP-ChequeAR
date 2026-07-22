using System;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using Moq;
using Xunit;

namespace ChequeAR.Tests.Application
{
    public class QuestionnaireResponseServiceTests
    {
        private readonly Mock<IQuestionnaireResponseRepository> _repo = new Mock<IQuestionnaireResponseRepository>();
        private readonly Mock<IQuestionnaireRepository> _qRepo = new Mock<IQuestionnaireRepository>();
        private readonly Mock<IFhirValidator> _validator = new Mock<IFhirValidator>();
        private readonly QuestionnaireResponseService _sut;

        public QuestionnaireResponseServiceTests()
        {
            _sut = new QuestionnaireResponseService(
                _repo.Object, _qRepo.Object, _validator.Object, TestHelpers.NoopAuditDispatcher());
        }

        // Los items llegan del cliente sin Id y EF no genera Guids. Sin asignarlos, dos
        // respuestas cualesquiera comparten Guid.Empty y la ficha entera revienta con
        // ORA-00001: sólo se podían guardar fichas de una sola pregunta.
        [Fact]
        public async Task CreateAsync_AssignsUniqueIdsToItems()
        {
            var questionnaireId = Guid.NewGuid();
            _qRepo.Setup(r => r.GetAsync(questionnaireId))
                  .ReturnsAsync(new Questionnaire { Id = questionnaireId, Status = QuestionnaireStatus.Active });

            var response = new QuestionnaireResponse
            {
                QuestionnaireId = questionnaireId,
                PatientHospitalId = "HC-1",
                Items =
                {
                    new QuestionnaireResponseItem { LinkId = "a" },
                    new QuestionnaireResponseItem { LinkId = "b" },
                    new QuestionnaireResponseItem { LinkId = "sintomas", ValueCode = "Dolor" },
                    new QuestionnaireResponseItem { LinkId = "sintomas", ValueCode = "Fatiga" }
                }
            };

            var created = await _sut.CreateAsync(response, "u");

            Assert.DoesNotContain(created.Items, i => i.Id == Guid.Empty);
            Assert.Equal(created.Items.Count, created.Items.Select(i => i.Id).Distinct().Count());
            Assert.All(created.Items, i => Assert.Equal(created.Id, i.ResponseId));
        }

        // Una opción múltiple genera varias filas con el MISMO linkId: eso es válido y no
        // debe deduplicarse, cada una es una opción marcada.
        [Fact]
        public async Task CreateAsync_KeepsRepeatedLinkIds()
        {
            var questionnaireId = Guid.NewGuid();
            _qRepo.Setup(r => r.GetAsync(questionnaireId))
                  .ReturnsAsync(new Questionnaire { Id = questionnaireId, Status = QuestionnaireStatus.Active });

            var response = new QuestionnaireResponse
            {
                QuestionnaireId = questionnaireId,
                PatientHospitalId = "HC-1",
                Items =
                {
                    new QuestionnaireResponseItem { LinkId = "sintomas", ValueCode = "Dolor" },
                    new QuestionnaireResponseItem { LinkId = "sintomas", ValueCode = "Fatiga" }
                }
            };

            var created = await _sut.CreateAsync(response, "u");

            Assert.Equal(2, created.Items.Count(i => i.LinkId == "sintomas"));
        }

        // No se pueden cargar respuestas contra un cuestionario archivado.
        [Fact]
        public async Task CreateAsync_RetiredQuestionnaire_Throws()
        {
            var questionnaireId = Guid.NewGuid();
            _qRepo.Setup(r => r.GetAsync(questionnaireId))
                  .ReturnsAsync(new Questionnaire { Id = questionnaireId, Status = QuestionnaireStatus.Retired });

            await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CreateAsync(
                new QuestionnaireResponse { QuestionnaireId = questionnaireId, PatientHospitalId = "HC-1" }, "u"));
        }
    }
}
