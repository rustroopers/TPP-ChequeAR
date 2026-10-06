using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Exceptions;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using Moq;
using Newtonsoft.Json.Linq;
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
                _repo.Object, _qRepo.Object, _validator.Object, TestHelpers.NoopAuditDispatcher(), TestHelpers.UnrestrictedGuard(), TestHelpers.UnrestrictedAccess());
        }

        // Los items llegan del cliente sin Id y EF no genera Guids. Sin asignarlos, dos
        // respuestas cualesquiera comparten Guid.Empty y la ficha entera revienta con
        // ORA-00001: sólo se podían guardar fichas de una sola pregunta.
        [Fact]
        public async Task CreateAsync_AssignsUniqueIdsToItems()
        {
            var questionnaireId = Guid.NewGuid();
            _qRepo.Setup(r => r.GetAsync(questionnaireId)).ReturnsAsync(Published(questionnaireId));

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
            _qRepo.Setup(r => r.GetAsync(questionnaireId)).ReturnsAsync(Published(questionnaireId));

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

        // FhirJson no lo escribia nadie: FinalizeAsync lo validaba, el validador lo
        // rechazaba por vacio y TODA finalizacion terminaba en 422. Ahora se arma al crear.
        [Fact]
        public async Task CreateAsync_BuildsFhirJson()
        {
            var questionnaireId = Guid.NewGuid();
            _qRepo.Setup(r => r.GetAsync(questionnaireId)).ReturnsAsync(
                Published(questionnaireId, "{\"resourceType\":\"Questionnaire\",\"item\":[{\"linkId\":\"edad\",\"text\":\"Edad\",\"type\":\"integer\"}]}"));

            var created = await _sut.CreateAsync(new QuestionnaireResponse
            {
                QuestionnaireId = questionnaireId,
                PatientHospitalId = "PAC-1",
                Items = { new QuestionnaireResponseItem { LinkId = "edad", ValueInteger = 7 } }
            }, "u");

            var fhir = JObject.Parse(created.FhirJson);
            Assert.Equal("QuestionnaireResponse", (string)fhir["resourceType"]);
            Assert.Equal("in-progress", (string)fhir["status"]);
            Assert.Equal("PAC-1", (string)fhir.SelectToken("subject.identifier.value"));
            Assert.Equal("Edad", (string)fhir.SelectToken("item[0].text"));
            Assert.Equal(7, (int)fhir.SelectToken("item[0].answer[0].valueInteger"));
        }

        // Al finalizar, el recurso se regenera con status completed: el guardado al crear
        // decia in-progress y quedaria contradiciendo el estado real.
        [Fact]
        public async Task FinalizeAsync_RegeneratesFhirJsonAsCompleted()
        {
            var id = Guid.NewGuid();
            var questionnaireId = Guid.NewGuid();
            var lockToken = Guid.NewGuid();
            var response = new QuestionnaireResponse
            {
                Id = id,
                QuestionnaireId = questionnaireId,
                PatientHospitalId = "PAC-1",
                Status = ResponseStatus.InProgress,
                FhirJson = "{\"resourceType\":\"QuestionnaireResponse\",\"status\":\"in-progress\"}",
                LockToken = lockToken,
                LockedByUserId = "u",
                LockExpiresAt = DateTime.UtcNow.AddMinutes(15)
            };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(response);
            _qRepo.Setup(r => r.GetAsync(questionnaireId)).ReturnsAsync(Published(questionnaireId));
            _validator.Setup(v => v.Validate(It.IsAny<string>(), It.IsAny<string>())).Returns(TestHelpers.ValidOk());

            var finalized = await _sut.FinalizeAsync(id, "u", lockToken);

            Assert.Equal(ResponseStatus.Completed, finalized.Status);
            Assert.Equal("completed", (string)JObject.Parse(finalized.FhirJson)["status"]);
        }

        // Al finalizar con éxito se libera el lock: ya no queda nada más que editar, y
        // deja de tener sentido que siga bloqueando a otros.
        [Fact]
        public async Task FinalizeAsync_ClearsLockOnSuccess()
        {
            var id = Guid.NewGuid();
            var questionnaireId = Guid.NewGuid();
            var lockToken = Guid.NewGuid();
            var response = new QuestionnaireResponse
            {
                Id = id,
                QuestionnaireId = questionnaireId,
                PatientHospitalId = "PAC-1",
                Status = ResponseStatus.InProgress,
                FhirJson = "{\"resourceType\":\"QuestionnaireResponse\",\"status\":\"in-progress\"}",
                LockToken = lockToken,
                LockedByUserId = "u",
                LockExpiresAt = DateTime.UtcNow.AddMinutes(15)
            };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(response);
            _qRepo.Setup(r => r.GetAsync(questionnaireId)).ReturnsAsync(Published(questionnaireId));
            _validator.Setup(v => v.Validate(It.IsAny<string>(), It.IsAny<string>())).Returns(TestHelpers.ValidOk());

            var finalized = await _sut.FinalizeAsync(id, "u", lockToken);

            Assert.Null(finalized.LockToken);
            Assert.Null(finalized.LockedByUserId);
        }

        private const string ConObligatoria =
            "{\"resourceType\":\"Questionnaire\",\"item\":[{\"linkId\":\"edad\",\"text\":\"Edad\",\"type\":\"integer\",\"required\":true}]}";

        // La validación de obligatorias vivía sólo en el navegador: una llamada directa a
        // $finalize cerraba fichas incompletas.
        [Fact]
        public async Task FinalizeAsync_ObligatoriaSinResponder_RechazaYQuedaEnCurso()
        {
            var (id, lockToken, response) = FichaConLock(ConObligatoria);

            var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => _sut.FinalizeAsync(id, "u", lockToken));

            Assert.Contains(ex.Errors, e => e.Contains("Edad"));
            Assert.Equal(ResponseStatus.InProgress, response.Status);
            Assert.Equal(lockToken, response.LockToken);
            _repo.Verify(r => r.UpdateAsync(It.IsAny<QuestionnaireResponse>()), Times.Never);
        }

        [Fact]
        public async Task FinalizeAsync_ObligatoriaRespondida_Finaliza()
        {
            var (id, lockToken, response) = FichaConLock(ConObligatoria);
            response.Items.Add(new QuestionnaireResponseItem { LinkId = "edad", ValueInteger = 7 });

            var finalized = await _sut.FinalizeAsync(id, "u", lockToken);

            Assert.Equal(ResponseStatus.Completed, finalized.Status);
        }

        // El autosave corre cada pocos segundos: tiene que leer sólo la versión anclada, no el
        // cuestionario entero con todas sus versiones, secciones, preguntas y opciones.
        [Fact]
        public async Task SaveProgressAsync_LeeSoloLaVersionAnclada()
        {
            var (id, lockToken, response) = FichaConLock(ConObligatoria);
            var versionId = Guid.NewGuid();
            response.QuestionnaireVersionId = versionId;
            _qRepo.Setup(r => r.GetVersionByIdAsync(response.QuestionnaireId, versionId))
                  .ReturnsAsync(new QuestionnaireVersion { Id = versionId, VersionNumber = 1, FhirJson = ConObligatoria });

            await _sut.SaveProgressAsync(id, "u", lockToken, null,
                new List<QuestionnaireResponseItem> { new QuestionnaireResponseItem { LinkId = "edad", ValueInteger = 7 } });

            _qRepo.Verify(r => r.GetAsync(It.IsAny<Guid>()), Times.Never);
            Assert.Equal("Edad", (string)JObject.Parse(response.FhirJson).SelectToken("item[0].text"));
        }

        private (Guid id, Guid lockToken, QuestionnaireResponse response) FichaConLock(string questionnaireJson)
        {
            var id = Guid.NewGuid();
            var questionnaireId = Guid.NewGuid();
            var lockToken = Guid.NewGuid();
            var response = new QuestionnaireResponse
            {
                Id = id,
                QuestionnaireId = questionnaireId,
                PatientHospitalId = "PAC-1",
                Status = ResponseStatus.InProgress,
                LockToken = lockToken,
                LockedByUserId = "u",
                LockExpiresAt = DateTime.UtcNow.AddMinutes(15)
            };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(response);
            _qRepo.Setup(r => r.GetAsync(questionnaireId)).ReturnsAsync(Published(questionnaireId, questionnaireJson));
            _validator.Setup(v => v.Validate(It.IsAny<string>(), It.IsAny<string>())).Returns(TestHelpers.ValidOk());
            return (id, lockToken, response);
        }

        // Si el lock lo tiene otro usuario, finalizar no puede cerrarle la sesión: se
        // rechaza igual que un guardado parcial.
        [Fact]
        public async Task FinalizeAsync_LockedByAnotherUser_Throws()
        {
            var id = Guid.NewGuid();
            var response = new QuestionnaireResponse
            {
                Id = id,
                Status = ResponseStatus.InProgress,
                LockToken = Guid.NewGuid(),
                LockedByUserId = "otro-medico",
                LockExpiresAt = DateTime.UtcNow.AddMinutes(15)
            };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(response);

            await Assert.ThrowsAsync<LockConflictException>(
                () => _sut.FinalizeAsync(id, "u", Guid.NewGuid()));
        }

        // Si el recurso no valida, la ficha NO queda finalizada a medias.
        [Fact]
        public async Task FinalizeAsync_Invalid_KeepsInProgress()
        {
            var id = Guid.NewGuid();
            var lockToken = Guid.NewGuid();
            var response = new QuestionnaireResponse
            {
                Id = id,
                Status = ResponseStatus.InProgress,
                LockToken = lockToken,
                LockedByUserId = "u",
                LockExpiresAt = DateTime.UtcNow.AddMinutes(15)
            };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(response);
            _qRepo.Setup(r => r.GetAsync(It.IsAny<Guid>())).ReturnsAsync((Questionnaire)null);
            _validator.Setup(v => v.Validate(It.IsAny<string>(), It.IsAny<string>()))
                      .Returns(TestHelpers.ValidFail("algo falta"));

            // Tipo especifico: la ficha no se finalizo por VALIDACION, no por una regla de
            // negocio. La distincion importa porque el controller responde distinto —un
            // issue por regla incumplida en lugar de un unico mensaje.
            var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => _sut.FinalizeAsync(id, "u", lockToken));
            Assert.Contains("algo falta", ex.Errors);

            Assert.Equal(ResponseStatus.InProgress, response.Status);
        }

        // "Empezar de nuevo" sobre un borrador InProgress: queda EnteredInError (no se
        // borra, para no perder el rastro de auditoría) y libera el lock si lo tenía.
        [Fact]
        public async Task AbandonAsync_MarksEnteredInErrorAndClearsLock()
        {
            var id = Guid.NewGuid();
            var questionnaireId = Guid.NewGuid();
            var response = new QuestionnaireResponse
            {
                Id = id,
                QuestionnaireId = questionnaireId,
                PatientHospitalId = "PAC-1",
                Status = ResponseStatus.InProgress,
                FhirJson = "{\"resourceType\":\"QuestionnaireResponse\",\"status\":\"in-progress\"}",
                LockToken = Guid.NewGuid(),
                LockedByUserId = "u",
                LockExpiresAt = DateTime.UtcNow.AddMinutes(15)
            };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(response);
            _qRepo.Setup(r => r.GetAsync(questionnaireId)).ReturnsAsync(Published(questionnaireId));

            var abandoned = await _sut.AbandonAsync(id, "u");

            Assert.Equal(ResponseStatus.EnteredInError, abandoned.Status);
            Assert.Equal("entered-in-error", (string)JObject.Parse(abandoned.FhirJson)["status"]);
            Assert.Null(abandoned.LockToken);
            Assert.Null(abandoned.LockedByUserId);
        }

        // Sin lock tomado (el caso normal: se descarta ANTES de entrar a editar, desde el
        // selector de paciente) también tiene que poder descartarse.
        [Fact]
        public async Task AbandonAsync_WithoutLock_Succeeds()
        {
            var id = Guid.NewGuid();
            var questionnaireId = Guid.NewGuid();
            var response = new QuestionnaireResponse
            {
                Id = id,
                QuestionnaireId = questionnaireId,
                PatientHospitalId = "PAC-1",
                Status = ResponseStatus.InProgress,
                FhirJson = "{\"resourceType\":\"QuestionnaireResponse\",\"status\":\"in-progress\"}"
            };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(response);
            _qRepo.Setup(r => r.GetAsync(questionnaireId)).ReturnsAsync(Published(questionnaireId));

            var abandoned = await _sut.AbandonAsync(id, "u");

            Assert.Equal(ResponseStatus.EnteredInError, abandoned.Status);
        }

        // Si otro médico la tiene bloqueada en este momento, no se puede descartar por
        // debajo: mismo criterio que cualquier otra escritura sobre una ficha en edición.
        [Fact]
        public async Task AbandonAsync_LockedByAnotherUser_Throws()
        {
            var id = Guid.NewGuid();
            var response = new QuestionnaireResponse
            {
                Id = id,
                Status = ResponseStatus.InProgress,
                LockToken = Guid.NewGuid(),
                LockedByUserId = "otro-medico",
                LockExpiresAt = DateTime.UtcNow.AddMinutes(15)
            };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(response);

            await Assert.ThrowsAsync<LockConflictException>(() => _sut.AbandonAsync(id, "u"));
        }

        // Una ficha ya finalizada no es un "borrador" que se pueda descartar.
        [Fact]
        public async Task AbandonAsync_NotInProgress_Throws()
        {
            var id = Guid.NewGuid();
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(new QuestionnaireResponse { Id = id, Status = ResponseStatus.Completed });

            await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.AbandonAsync(id, "u"));
        }

        // EL CASO REPORTADO: se empieza a completar con la v1 y alguien publica la v2 en el
        // medio. La ficha tiene que quedar anclada a la v1 y conservar SUS textos, no
        // ajustarse a la version nueva.
        [Fact]
        public async Task CreateAsync_PinsToTheVersionTheClientRendered_NotTheCurrentOne()
        {
            var questionnaireId = Guid.NewGuid();
            var v1 = Guid.NewGuid();
            var v2 = Guid.NewGuid();
            var q = new Questionnaire { Id = questionnaireId, Status = QuestionnaireStatus.Active, CurrentVersionId = v2 };
            q.Versions.Add(new QuestionnaireVersion
            {
                Id = v1, VersionNumber = 1,
                FhirJson = "{\"resourceType\":\"Questionnaire\",\"item\":[{\"linkId\":\"p\",\"text\":\"Texto VIEJO\",\"type\":\"string\"}]}"
            });
            q.Versions.Add(new QuestionnaireVersion
            {
                Id = v2, VersionNumber = 2,
                FhirJson = "{\"resourceType\":\"Questionnaire\",\"item\":[{\"linkId\":\"p\",\"text\":\"Texto NUEVO\",\"type\":\"string\"}]}"
            });
            _qRepo.Setup(r => r.GetAsync(questionnaireId)).ReturnsAsync(q);

            // El medico abrio la ficha con la v1, aunque ya se publico la v2.
            var created = await _sut.CreateAsync(new QuestionnaireResponse
            {
                QuestionnaireId = questionnaireId,
                QuestionnaireVersionId = v1,
                PatientHospitalId = "PAC-1",
                Items = { new QuestionnaireResponseItem { LinkId = "p", ValueString = "respuesta" } }
            }, "u");

            Assert.Equal(v1, created.QuestionnaireVersionId);
            Assert.Equal("Texto VIEJO", (string)JObject.Parse(created.FhirJson).SelectToken("item[0].text"));
        }

        // Sin version indicada (cliente viejo o llamada por API) se cae a la vigente.
        [Fact]
        public async Task CreateAsync_NoVersionGiven_FallsBackToCurrent()
        {
            var questionnaireId = Guid.NewGuid();
            var current = Guid.NewGuid();
            _qRepo.Setup(r => r.GetAsync(questionnaireId)).ReturnsAsync(Published(questionnaireId, null, current));

            var created = await _sut.CreateAsync(new QuestionnaireResponse
            {
                QuestionnaireId = questionnaireId,
                PatientHospitalId = "PAC-1"
            }, "u");

            Assert.Equal(current, created.QuestionnaireVersionId);
        }

        // Un cuestionario sin publicar no se puede responder: no hay nada congelado.
        [Fact]
        public async Task CreateAsync_NeverPublished_Throws()
        {
            var questionnaireId = Guid.NewGuid();
            _qRepo.Setup(r => r.GetAsync(questionnaireId))
                  .ReturnsAsync(new Questionnaire { Id = questionnaireId, Status = QuestionnaireStatus.Draft });

            await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.CreateAsync(
                new QuestionnaireResponse { QuestionnaireId = questionnaireId, PatientHospitalId = "PAC-1" }, "u"));
        }

        // El recurso se regenera al crear, pero los datos del paciente que venian embebidos
        // NO se pierden: cuando el hospital no responde, ese Patient "contained" es la unica
        // forma de saber de quien es la ficha.
        [Fact]
        public async Task CreateAsync_PreservesContainedPatient()
        {
            var questionnaireId = Guid.NewGuid();
            _qRepo.Setup(r => r.GetAsync(questionnaireId)).ReturnsAsync(Published(questionnaireId));

            var entrante = "{\"resourceType\":\"QuestionnaireResponse\",\"contained\":[{\"resourceType\":\"Patient\"," +
                           "\"name\":[{\"text\":\"Gómez, Martina\"}],\"birthDate\":\"2016-03-12\",\"gender\":\"female\"}]}";

            var created = await _sut.CreateAsync(new QuestionnaireResponse
            {
                QuestionnaireId = questionnaireId,
                PatientHospitalId = "458821",
                FhirJson = entrante
            }, "u");

            var fhir = JObject.Parse(created.FhirJson);
            Assert.Equal("Gómez, Martina", (string)fhir.SelectToken("contained[0].name[0].text"));
            Assert.Equal("2016-03-12", (string)fhir.SelectToken("contained[0].birthDate"));
            Assert.Equal("Gómez, Martina", (string)fhir.SelectToken("subject.display"));
        }

        // Sin datos de paciente NO se inventa un Patient contenido. El recurso escribe la HC
        // en subject.display cuando no hay nombre; si al regenerarlo se leyera ese display
        // como nombre, finalizar terminaba fabricando un paciente llamado "PAC-000001".
        [Fact]
        public async Task FinalizeAsync_NoPatientData_DoesNotInventContainedPatient()
        {
            var id = Guid.NewGuid();
            var questionnaireId = Guid.NewGuid();
            var lockToken = Guid.NewGuid();
            var response = new QuestionnaireResponse
            {
                Id = id,
                QuestionnaireId = questionnaireId,
                PatientHospitalId = "PAC-000001",
                Status = ResponseStatus.InProgress,
                // Recurso tal como lo deja CreateAsync cuando el hospital resuelve al
                // paciente: sin contained, con la HC como display.
                FhirJson = "{\"resourceType\":\"QuestionnaireResponse\",\"subject\":{\"display\":\"PAC-000001\"}}",
                LockToken = lockToken,
                LockedByUserId = "u",
                LockExpiresAt = DateTime.UtcNow.AddMinutes(15)
            };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(response);
            _qRepo.Setup(r => r.GetAsync(questionnaireId)).ReturnsAsync(Published(questionnaireId));
            _validator.Setup(v => v.Validate(It.IsAny<string>(), It.IsAny<string>())).Returns(TestHelpers.ValidOk());

            var finalized = await _sut.FinalizeAsync(id, "u", lockToken);

            Assert.Null(JObject.Parse(finalized.FhirJson)["contained"]);
        }

        // No se pueden cargar respuestas contra un cuestionario archivado.
        [Fact]
        public async Task CreateAsync_RetiredQuestionnaire_Throws()
        {
            var questionnaireId = Guid.NewGuid();
            _qRepo.Setup(r => r.GetAsync(questionnaireId))
                  .ReturnsAsync(new Questionnaire { Id = questionnaireId, Status = QuestionnaireStatus.Retired });

            await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.CreateAsync(
                new QuestionnaireResponse { QuestionnaireId = questionnaireId, PatientHospitalId = "HC-1" }, "u"));
        }
        // --- Lock de sesión de edición -----------------------------------------------

        [Fact]
        public async Task AcquireLockAsync_FreeLock_Succeeds()
        {
            var id = Guid.NewGuid();
            var response = new QuestionnaireResponse { Id = id, Status = ResponseStatus.InProgress };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(response);

            var locked = await _sut.AcquireLockAsync(id, "u");

            Assert.NotNull(locked.LockToken);
            Assert.Equal("u", locked.LockedByUserId);
            Assert.True(locked.LockExpiresAt > DateTime.UtcNow);
        }

        [Fact]
        public async Task AcquireLockAsync_HeldByAnotherUserAndStillValid_Throws()
        {
            var id = Guid.NewGuid();
            var response = new QuestionnaireResponse
            {
                Id = id,
                Status = ResponseStatus.InProgress,
                LockToken = Guid.NewGuid(),
                LockedByUserId = "otro-medico",
                LockExpiresAt = DateTime.UtcNow.AddMinutes(10)
            };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(response);

            var ex = await Assert.ThrowsAsync<LockConflictException>(() => _sut.AcquireLockAsync(id, "u"));
            Assert.Equal("otro-medico", ex.LockedByUserId);
        }

        // El TTL protege contra OTRO usuario pisando el trabajo, no bloquea el cuestionario
        // para siempre si el que lo tenía nunca volvió (llamada cortada, pestaña cerrada).
        [Fact]
        public async Task AcquireLockAsync_ExpiredLock_CanBeRetakenByAnotherUser()
        {
            var id = Guid.NewGuid();
            var response = new QuestionnaireResponse
            {
                Id = id,
                Status = ResponseStatus.InProgress,
                LockToken = Guid.NewGuid(),
                LockedByUserId = "otro-medico",
                LockExpiresAt = DateTime.UtcNow.AddMinutes(-1)
            };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(response);

            var locked = await _sut.AcquireLockAsync(id, "u");

            Assert.Equal("u", locked.LockedByUserId);
        }

        [Fact]
        public async Task AcquireLockAsync_NotInProgress_Throws()
        {
            var id = Guid.NewGuid();
            var response = new QuestionnaireResponse { Id = id, Status = ResponseStatus.Completed };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(response);

            await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.AcquireLockAsync(id, "u"));
        }

        [Fact]
        public async Task RenewLockAsync_ExtendsExpiry()
        {
            var id = Guid.NewGuid();
            var token = Guid.NewGuid();
            var response = new QuestionnaireResponse
            {
                Id = id,
                Status = ResponseStatus.InProgress,
                LockToken = token,
                LockedByUserId = "u",
                LockExpiresAt = DateTime.UtcNow.AddSeconds(30)
            };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(response);

            var renewed = await _sut.RenewLockAsync(id, "u", token);

            Assert.True(renewed.LockExpiresAt > DateTime.UtcNow.AddMinutes(10));
        }

        [Fact]
        public async Task RenewLockAsync_OtherUsersLock_Throws()
        {
            var id = Guid.NewGuid();
            var response = new QuestionnaireResponse
            {
                Id = id,
                Status = ResponseStatus.InProgress,
                LockToken = Guid.NewGuid(),
                LockedByUserId = "otro-medico",
                LockExpiresAt = DateTime.UtcNow.AddMinutes(10)
            };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(response);

            await Assert.ThrowsAsync<LockConflictException>(() => _sut.RenewLockAsync(id, "u", Guid.NewGuid()));
        }

        [Fact]
        public async Task ReleaseLockAsync_OwnLock_Clears()
        {
            var id = Guid.NewGuid();
            var token = Guid.NewGuid();
            var response = new QuestionnaireResponse
            {
                Id = id,
                Status = ResponseStatus.InProgress,
                LockToken = token,
                LockedByUserId = "u",
                LockExpiresAt = DateTime.UtcNow.AddMinutes(10)
            };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(response);

            await _sut.ReleaseLockAsync(id, "u", token);

            Assert.Null(response.LockToken);
            Assert.Null(response.LockedByUserId);
        }

        // Idempotente a propósito: liberar un lock que ya no es tuyo (te lo ganaron, o ya
        // estaba libre) no debe ser un error para quien perdió la sesión.
        [Fact]
        public async Task ReleaseLockAsync_NotOwner_IsNoop()
        {
            var id = Guid.NewGuid();
            var response = new QuestionnaireResponse
            {
                Id = id,
                Status = ResponseStatus.InProgress,
                LockToken = Guid.NewGuid(),
                LockedByUserId = "otro-medico",
                LockExpiresAt = DateTime.UtcNow.AddMinutes(10)
            };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(response);

            await _sut.ReleaseLockAsync(id, "u", Guid.NewGuid());

            Assert.Equal("otro-medico", response.LockedByUserId);
        }

        // --- Guardado parcial ----------------------------------------------------------

        // El corazón del completado asincrónico: una llamada de seguimiento sólo manda las
        // respuestas de ESA llamada, y lo respondido en llamadas anteriores no puede
        // desaparecer del recurso.
        [Fact]
        public async Task SaveProgressAsync_MergesByLinkId_PreservesUntouchedAnswers()
        {
            var id = Guid.NewGuid();
            var questionnaireId = Guid.NewGuid();
            var token = Guid.NewGuid();
            var response = new QuestionnaireResponse
            {
                Id = id,
                QuestionnaireId = questionnaireId,
                Status = ResponseStatus.InProgress,
                LockToken = token,
                LockedByUserId = "u",
                LockExpiresAt = DateTime.UtcNow.AddMinutes(10),
                Items = { new QuestionnaireResponseItem { Id = Guid.NewGuid(), ResponseId = id, LinkId = "peso", ValueDecimal = 70 } }
            };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(response);
            _qRepo.Setup(r => r.GetAsync(questionnaireId)).ReturnsAsync(Published(questionnaireId));

            var saved = await _sut.SaveProgressAsync(id, "u", token, null,
                new List<QuestionnaireResponseItem> { new QuestionnaireResponseItem { LinkId = "presion", ValueString = "120/80" } });

            var fhirLinkIds = JObject.Parse(saved.FhirJson)["item"].Select(i => (string)i["linkId"]).ToList();
            Assert.Contains("peso", fhirLinkIds);
            Assert.Contains("presion", fhirLinkIds);
            Assert.Equal(ResponseStatus.InProgress, saved.Status);
            Assert.True(saved.IsAutosave);
        }

        // El autosave NO audita: si lo hiciera, cada pocos segundos habría un evento nuevo.
        [Fact]
        public async Task SaveProgressAsync_DoesNotAudit()
        {
            var id = Guid.NewGuid();
            var questionnaireId = Guid.NewGuid();
            var token = Guid.NewGuid();
            var response = new QuestionnaireResponse
            {
                Id = id,
                QuestionnaireId = questionnaireId,
                Status = ResponseStatus.InProgress,
                LockToken = token,
                LockedByUserId = "u",
                LockExpiresAt = DateTime.UtcNow.AddMinutes(10)
            };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(response);
            _qRepo.Setup(r => r.GetAsync(questionnaireId)).ReturnsAsync(Published(questionnaireId));

            var captured = CapturingAudit(out var events);
            var sut = new QuestionnaireResponseService(_repo.Object, _qRepo.Object, _validator.Object, captured, TestHelpers.UnrestrictedGuard(), TestHelpers.UnrestrictedAccess());

            await sut.SaveProgressAsync(id, "u", token, null,
                new List<QuestionnaireResponseItem> { new QuestionnaireResponseItem { LinkId = "peso", ValueDecimal = 70 } });
            await captured.FlushAsync();

            Assert.Empty(events);
        }

        // El diff de "lo completado" se registra UNA vez, al cerrar la sesión deliberadamente
        // (Guardar avance): compara el baseline con el que se abrió contra lo que quedó.
        [Fact]
        public async Task ReleaseLockAsync_WithBaseline_AuditsNetSessionDiff()
        {
            var id = Guid.NewGuid();
            var questionnaireId = Guid.NewGuid();
            var token = Guid.NewGuid();
            var response = new QuestionnaireResponse
            {
                Id = id,
                QuestionnaireId = questionnaireId,
                Status = ResponseStatus.InProgress,
                LockToken = token,
                LockedByUserId = "u",
                LockExpiresAt = DateTime.UtcNow.AddMinutes(10),
                // Estado actual guardado (lo que dejó el último autosave de la sesión).
                Items =
                {
                    new QuestionnaireResponseItem { Id = Guid.NewGuid(), ResponseId = id, LinkId = "peso", ValueDecimal = 75 },
                    new QuestionnaireResponseItem { Id = Guid.NewGuid(), ResponseId = id, LinkId = "presion", ValueString = "120/80" }
                }
            };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(response);
            var fhir = "{\"resourceType\":\"Questionnaire\",\"item\":[" +
                       "{\"linkId\":\"peso\",\"text\":\"Peso (kg)\",\"type\":\"decimal\"}," +
                       "{\"linkId\":\"presion\",\"text\":\"Presión arterial\",\"type\":\"string\"}]}";
            _qRepo.Setup(r => r.GetAsync(questionnaireId)).ReturnsAsync(Published(questionnaireId, fhir));

            var captured = CapturingAudit(out var events);
            var sut = new QuestionnaireResponseService(_repo.Object, _qRepo.Object, _validator.Object, captured, TestHelpers.UnrestrictedGuard(), TestHelpers.UnrestrictedAccess());

            // Baseline: al abrir la sesión el peso era 70 y no había presión.
            var baseline = new List<QuestionnaireResponseItem>
            {
                new QuestionnaireResponseItem { LinkId = "peso", ValueDecimal = 70 }
            };
            await sut.ReleaseLockAsync(id, "u", token, baseline);
            await captured.FlushAsync();

            var save = events.Single(e => (e.EntityRef ?? "").Contains("$save"));
            var changes = Newtonsoft.Json.JsonConvert.DeserializeObject<List<ResponseFieldChange>>(save.FhirJson);
            var peso = changes.Single(c => c.LinkId == "peso");
            Assert.Equal("Peso (kg)", peso.Text);
            Assert.Equal("70", peso.Before);
            Assert.Equal("75", peso.After);
            var presion = changes.Single(c => c.LinkId == "presion");
            Assert.Equal("—", presion.Before);
            Assert.Equal("120/80", presion.After);
        }

        // Cierre pasivo (sin baseline, ej. cerrar la pestaña): sólo libera el lock, no audita.
        [Fact]
        public async Task ReleaseLockAsync_WithoutBaseline_DoesNotAudit()
        {
            var id = Guid.NewGuid();
            var token = Guid.NewGuid();
            var response = new QuestionnaireResponse
            {
                Id = id,
                Status = ResponseStatus.InProgress,
                LockToken = token,
                LockedByUserId = "u",
                LockExpiresAt = DateTime.UtcNow.AddMinutes(10)
            };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(response);

            var captured = CapturingAudit(out var events);
            var sut = new QuestionnaireResponseService(_repo.Object, _qRepo.Object, _validator.Object, captured, TestHelpers.UnrestrictedGuard(), TestHelpers.UnrestrictedAccess());

            await sut.ReleaseLockAsync(id, "u", token);   // sin baseline
            await captured.FlushAsync();

            Assert.Empty(events);
            Assert.Null(response.LockToken);   // igual liberó el lock
        }

        // Al finalizar, el evento lleva la foto COMPLETA de la ficha (todas las preguntas con
        // su respuesta final), no sólo lo que cambió en la última sesión.
        [Fact]
        public async Task FinalizeAsync_AttachesFullSnapshot()
        {
            var id = Guid.NewGuid();
            var questionnaireId = Guid.NewGuid();
            var token = Guid.NewGuid();
            var response = new QuestionnaireResponse
            {
                Id = id,
                QuestionnaireId = questionnaireId,
                Status = ResponseStatus.InProgress,
                LockToken = token,
                LockedByUserId = "u",
                LockExpiresAt = DateTime.UtcNow.AddMinutes(10),
                Items = { new QuestionnaireResponseItem { Id = Guid.NewGuid(), ResponseId = id, LinkId = "peso", ValueDecimal = 80 } }
            };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(response);
            // Dos preguntas: una respondida (peso) y una sin responder (altura) -> debe salir "—".
            var fhir = "{\"resourceType\":\"Questionnaire\",\"item\":[" +
                       "{\"linkId\":\"g1\",\"text\":\"Antropometría\",\"type\":\"group\",\"item\":[" +
                       "{\"linkId\":\"peso\",\"text\":\"Peso (kg)\",\"type\":\"decimal\"}," +
                       "{\"linkId\":\"altura\",\"text\":\"Altura (cm)\",\"type\":\"decimal\"}]}]}";
            _qRepo.Setup(r => r.GetAsync(questionnaireId)).ReturnsAsync(Published(questionnaireId, fhir));
            _validator.Setup(v => v.Validate(It.IsAny<string>(), It.IsAny<string>())).Returns(TestHelpers.ValidOk());

            var captured = CapturingAudit(out var events);
            var sut = new QuestionnaireResponseService(_repo.Object, _qRepo.Object, _validator.Object, captured, TestHelpers.UnrestrictedGuard(), TestHelpers.UnrestrictedAccess());

            await sut.FinalizeAsync(id, "u", token);
            await captured.FlushAsync();

            var fin = events.Single(e => (e.EntityRef ?? "").Contains("$finalize"));
            var sections = Newtonsoft.Json.JsonConvert.DeserializeObject<List<ResponseSnapshotSection>>(fin.FhirJson);
            var answers = sections.SelectMany(s => s.Answers).ToList();
            Assert.Equal("80", answers.Single(a => a.Text == "Peso (kg)").Answer);
            Assert.Equal("—", answers.Single(a => a.Text == "Altura (cm)").Answer);   // sin responder
        }

        // Al finalizar mandando el baseline de la sesión, además de la foto completa se audita
        // el diff de lo que cambió en esa última tanda (A→B), igual que "Guardar avance".
        [Fact]
        public async Task FinalizeAsync_WithBaseline_AlsoAuditsSessionDiff()
        {
            var id = Guid.NewGuid();
            var questionnaireId = Guid.NewGuid();
            var token = Guid.NewGuid();
            var response = new QuestionnaireResponse
            {
                Id = id,
                QuestionnaireId = questionnaireId,
                Status = ResponseStatus.InProgress,
                LockToken = token,
                LockedByUserId = "u",
                LockExpiresAt = DateTime.UtcNow.AddMinutes(10),
                // Estado final guardado: el peso quedó en 80.
                Items = { new QuestionnaireResponseItem { Id = Guid.NewGuid(), ResponseId = id, LinkId = "peso", ValueDecimal = 80 } }
            };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(response);
            var fhir = "{\"resourceType\":\"Questionnaire\",\"item\":[" +
                       "{\"linkId\":\"peso\",\"text\":\"Peso (kg)\",\"type\":\"decimal\"}]}";
            _qRepo.Setup(r => r.GetAsync(questionnaireId)).ReturnsAsync(Published(questionnaireId, fhir));
            _validator.Setup(v => v.Validate(It.IsAny<string>(), It.IsAny<string>())).Returns(TestHelpers.ValidOk());

            var captured = CapturingAudit(out var events);
            var sut = new QuestionnaireResponseService(_repo.Object, _qRepo.Object, _validator.Object, captured, TestHelpers.UnrestrictedGuard(), TestHelpers.UnrestrictedAccess());

            // Baseline: al abrir la sesión el peso era 70; se finaliza con 80 -> diff 70→80.
            var baseline = new List<QuestionnaireResponseItem>
            {
                new QuestionnaireResponseItem { LinkId = "peso", ValueDecimal = 70 }
            };
            await sut.FinalizeAsync(id, "u", token, baseline);
            await captured.FlushAsync();

            var save = events.Single(e => (e.EntityRef ?? "").Contains("$save"));
            var changes = Newtonsoft.Json.JsonConvert.DeserializeObject<List<ResponseFieldChange>>(save.FhirJson);
            var peso = changes.Single(c => c.LinkId == "peso");
            Assert.Equal("70", peso.Before);
            Assert.Equal("80", peso.After);
            // Y además queda el evento de finalización con la foto completa.
            Assert.Contains(events, e => (e.EntityRef ?? "").Contains("$finalize"));
        }

        // Finalizar sin baseline (fichas viejas / cliente que no lo manda): sólo la foto
        // completa, sin evento de diff de sesión.
        [Fact]
        public async Task FinalizeAsync_WithoutBaseline_NoSessionDiff()
        {
            var id = Guid.NewGuid();
            var questionnaireId = Guid.NewGuid();
            var token = Guid.NewGuid();
            var response = new QuestionnaireResponse
            {
                Id = id,
                QuestionnaireId = questionnaireId,
                Status = ResponseStatus.InProgress,
                LockToken = token,
                LockedByUserId = "u",
                LockExpiresAt = DateTime.UtcNow.AddMinutes(10),
                Items = { new QuestionnaireResponseItem { Id = Guid.NewGuid(), ResponseId = id, LinkId = "peso", ValueDecimal = 80 } }
            };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(response);
            var fhir = "{\"resourceType\":\"Questionnaire\",\"item\":[" +
                       "{\"linkId\":\"peso\",\"text\":\"Peso (kg)\",\"type\":\"decimal\"}]}";
            _qRepo.Setup(r => r.GetAsync(questionnaireId)).ReturnsAsync(Published(questionnaireId, fhir));
            _validator.Setup(v => v.Validate(It.IsAny<string>(), It.IsAny<string>())).Returns(TestHelpers.ValidOk());

            var captured = CapturingAudit(out var events);
            var sut = new QuestionnaireResponseService(_repo.Object, _qRepo.Object, _validator.Object, captured, TestHelpers.UnrestrictedGuard(), TestHelpers.UnrestrictedAccess());

            await sut.FinalizeAsync(id, "u", token);   // sin baseline
            await captured.FlushAsync();

            Assert.DoesNotContain(events, e => (e.EntityRef ?? "").Contains("$save"));
            Assert.Contains(events, e => (e.EntityRef ?? "").Contains("$finalize"));
        }

        // --- Pasar a la versión vigente (retarget) -------------------------------------

        // v1: [edad, rigidez]   v2 (vigente): [edad (enunciado cambiado), score]
        private const string RetargetV1 =
            "{\"resourceType\":\"Questionnaire\",\"item\":[" +
            "{\"linkId\":\"edad\",\"text\":\"Edad\",\"type\":\"integer\"}," +
            "{\"linkId\":\"rigidez\",\"text\":\"Rigidez matutina\",\"type\":\"boolean\"}]}";
        private const string RetargetV2 =
            "{\"resourceType\":\"Questionnaire\",\"item\":[" +
            "{\"linkId\":\"edad\",\"text\":\"Edad (años)\",\"type\":\"integer\"}," +
            "{\"linkId\":\"score\",\"text\":\"Score BVAS\",\"type\":\"integer\"}]}";

        private static Questionnaire TwoVersions(Guid id, out Guid v1Id, out Guid v2Id)
        {
            v1Id = Guid.NewGuid();
            v2Id = Guid.NewGuid();
            var q = new Questionnaire { Id = id, Status = QuestionnaireStatus.Active, CurrentVersionId = v2Id };
            q.Versions.Add(new QuestionnaireVersion { Id = v1Id, VersionNumber = 1, FhirJson = RetargetV1 });
            q.Versions.Add(new QuestionnaireVersion { Id = v2Id, VersionNumber = 2, FhirJson = RetargetV2 });
            return q;
        }

        private static QuestionnaireResponse PinnedToV1(Guid id, Guid questionnaireId, Guid v1Id, Guid lockToken = default)
        {
            return new QuestionnaireResponse
            {
                Id = id,
                QuestionnaireId = questionnaireId,
                QuestionnaireVersionId = v1Id,
                Status = ResponseStatus.InProgress,
                LockToken = lockToken == default ? (Guid?)null : lockToken,
                LockedByUserId = lockToken == default ? null : "u",
                LockExpiresAt = lockToken == default ? (DateTime?)null : DateTime.UtcNow.AddMinutes(10),
                Items =
                {
                    new QuestionnaireResponseItem { Id = Guid.NewGuid(), ResponseId = id, LinkId = "edad", ValueInteger = 40 },
                    new QuestionnaireResponseItem { Id = Guid.NewGuid(), ResponseId = id, LinkId = "rigidez", ValueBoolean = true }
                }
            };
        }

        [Fact]
        public async Task BuildRetargetPreviewAsync_ClassifiesCarriedAddedDropped()
        {
            var id = Guid.NewGuid();
            var questionnaireId = Guid.NewGuid();
            var q = TwoVersions(questionnaireId, out var v1Id, out _);
            _qRepo.Setup(r => r.GetAsync(questionnaireId)).ReturnsAsync(q);
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(PinnedToV1(id, questionnaireId, v1Id));

            var preview = await _sut.BuildRetargetPreviewAsync(id);

            Assert.False(preview.AlreadyCurrent);
            Assert.Equal(1, preview.FromVersion);
            Assert.Equal(2, preview.ToVersion);
            var carried = Assert.Single(preview.Carried);
            Assert.Equal("edad", carried.LinkId);
            Assert.True(carried.Changed);                       // "Edad" -> "Edad (años)"
            Assert.Equal("score", Assert.Single(preview.Added).LinkId);
            var dropped = Assert.Single(preview.Dropped);       // rigidez ya no existe en v2 y tenía respuesta
            Assert.Equal("rigidez", dropped.LinkId);
            Assert.Equal("Sí", dropped.Answer);
        }

        [Fact]
        public async Task RetargetToCurrentVersionAsync_RepinsKeepsMatchingDropsRemovedAndAudits()
        {
            var id = Guid.NewGuid();
            var questionnaireId = Guid.NewGuid();
            var token = Guid.NewGuid();
            var q = TwoVersions(questionnaireId, out var v1Id, out var v2Id);
            _qRepo.Setup(r => r.GetAsync(questionnaireId)).ReturnsAsync(q);
            var response = PinnedToV1(id, questionnaireId, v1Id, token);
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(response);

            ICollection<string> keep = null;
            _repo.Setup(r => r.RetargetAsync(response, It.IsAny<ICollection<string>>()))
                 .Callback<QuestionnaireResponse, ICollection<string>>((_, k) => keep = k)
                 .Returns(Task.CompletedTask);

            var captured = CapturingAudit(out var events);
            var sut = new QuestionnaireResponseService(_repo.Object, _qRepo.Object, _validator.Object, captured, TestHelpers.UnrestrictedGuard(), TestHelpers.UnrestrictedAccess());

            var result = await sut.RetargetToCurrentVersionAsync(id, "u", token);
            await captured.FlushAsync();

            Assert.Equal(v2Id, result.QuestionnaireVersionId);          // re-anclada a la vigente
            Assert.Contains("edad", keep);                              // conservadas: las de v2
            Assert.Contains("score", keep);
            Assert.DoesNotContain("rigidez", keep);                     // descartada: no está en v2
            var evt = events.Single(e => (e.EntityRef ?? "").Contains("$retarget"));
            var dropped = Newtonsoft.Json.JsonConvert.DeserializeObject<List<ResponseFieldChange>>(evt.FhirJson);
            Assert.Equal("rigidez", Assert.Single(dropped).LinkId);
        }

        /// <summary>Dispatcher que acumula lo que se persiste, para poder inspeccionar el diff adjunto.</summary>
        private static AuditDispatcher CapturingAudit(out List<AuditEvent> events)
        {
            var sink = new List<AuditEvent>();
            events = sink;
            var repo = new Mock<IAuditRepository>();
            // El batch diferido (flush por timer) para eventos vía Enqueue…
            repo.Setup(r => r.AddManyAsync(It.IsAny<IEnumerable<AuditEvent>>()))
                .Callback<IEnumerable<AuditEvent>>(batch => sink.AddRange(batch))
                .Returns(Task.CompletedTask);
            // …y la persistencia inmediata (PersistImmediatelyAsync) para finalizar/guardar avance.
            repo.Setup(r => r.AddAsync(It.IsAny<AuditEvent>()))
                .Callback<AuditEvent>(evt => sink.Add(evt))
                .Returns(Task.CompletedTask);
            return new AuditDispatcher(repo.Object, flushIntervalMs: 600000);
        }

        [Fact]
        public async Task SaveProgressAsync_AssignsIdsAndResponseIdToIncomingItems()
        {
            var id = Guid.NewGuid();
            var questionnaireId = Guid.NewGuid();
            var token = Guid.NewGuid();
            var response = new QuestionnaireResponse
            {
                Id = id,
                QuestionnaireId = questionnaireId,
                Status = ResponseStatus.InProgress,
                LockToken = token,
                LockedByUserId = "u",
                LockExpiresAt = DateTime.UtcNow.AddMinutes(10)
            };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(response);
            _qRepo.Setup(r => r.GetAsync(questionnaireId)).ReturnsAsync(Published(questionnaireId));

            ICollection<QuestionnaireResponseItem> persisted = null;
            _repo.Setup(r => r.SaveProgressAsync(response, It.IsAny<ICollection<QuestionnaireResponseItem>>()))
                 .Callback<QuestionnaireResponse, ICollection<QuestionnaireResponseItem>>((_, items) => persisted = items)
                 .Returns(Task.CompletedTask);

            await _sut.SaveProgressAsync(id, "u", token, null,
                new List<QuestionnaireResponseItem> { new QuestionnaireResponseItem { LinkId = "presion", ValueString = "120/80" } });

            Assert.NotNull(persisted);
            Assert.All(persisted, i => Assert.NotEqual(Guid.Empty, i.Id));
            Assert.All(persisted, i => Assert.Equal(id, i.ResponseId));
        }

        [Fact]
        public async Task SaveProgressAsync_WithoutAnyLockTaken_Throws()
        {
            var id = Guid.NewGuid();
            var response = new QuestionnaireResponse { Id = id, Status = ResponseStatus.InProgress };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(response);

            await Assert.ThrowsAsync<BusinessRuleException>(
                () => _sut.SaveProgressAsync(id, "u", Guid.NewGuid(), null, new List<QuestionnaireResponseItem>()));
        }

        [Fact]
        public async Task SaveProgressAsync_LockedByAnotherUser_Throws()
        {
            var id = Guid.NewGuid();
            var response = new QuestionnaireResponse
            {
                Id = id,
                Status = ResponseStatus.InProgress,
                LockToken = Guid.NewGuid(),
                LockedByUserId = "otro-medico",
                LockExpiresAt = DateTime.UtcNow.AddMinutes(10)
            };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(response);

            await Assert.ThrowsAsync<LockConflictException>(
                () => _sut.SaveProgressAsync(id, "u", Guid.NewGuid(), null, new List<QuestionnaireResponseItem>()));
        }

        // EL CASO DEL GRACE PERIOD: al médico se le venció el TTL a mitad de la llamada,
        // pero nadie más tomó el cuestionario en el medio. El guardado tiene que entrar
        // igual y renovar en silencio, en vez de tirar todo lo que estaba tipeando.
        [Fact]
        public async Task SaveProgressAsync_OwnExpiredLock_NobodyElseTookIt_SucceedsAndRenews()
        {
            var id = Guid.NewGuid();
            var questionnaireId = Guid.NewGuid();
            var token = Guid.NewGuid();
            var response = new QuestionnaireResponse
            {
                Id = id,
                QuestionnaireId = questionnaireId,
                Status = ResponseStatus.InProgress,
                LockToken = token,
                LockedByUserId = "u",
                LockExpiresAt = DateTime.UtcNow.AddMinutes(-5)   // venció, pero sigue siendo EL MISMO token/usuario
            };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(response);
            _qRepo.Setup(r => r.GetAsync(questionnaireId)).ReturnsAsync(Published(questionnaireId));

            var saved = await _sut.SaveProgressAsync(id, "u", token, null, new List<QuestionnaireResponseItem>());

            Assert.True(saved.LockExpiresAt > DateTime.UtcNow);
        }

        // Si en cambio otro usuario YA tomó el lock (token distinto), no hay grace period
        // que valga: se rechaza, aunque el que llega tarde crea que todavía es su sesión.
        [Fact]
        public async Task SaveProgressAsync_ExpiredLockButTakenByAnotherUser_Throws()
        {
            var id = Guid.NewGuid();
            var oldToken = Guid.NewGuid();
            var response = new QuestionnaireResponse
            {
                Id = id,
                Status = ResponseStatus.InProgress,
                LockToken = Guid.NewGuid(),   // otro usuario ya lo re-tomó con un token nuevo
                LockedByUserId = "otro-medico",
                LockExpiresAt = DateTime.UtcNow.AddMinutes(10)
            };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(response);

            await Assert.ThrowsAsync<LockConflictException>(
                () => _sut.SaveProgressAsync(id, "u", oldToken, null, new List<QuestionnaireResponseItem>()));
        }

        [Fact]
        public async Task SaveProgressAsync_StaleConcurrencyVersion_Throws()
        {
            var id = Guid.NewGuid();
            var token = Guid.NewGuid();
            var response = new QuestionnaireResponse
            {
                Id = id,
                Status = ResponseStatus.InProgress,
                LockToken = token,
                LockedByUserId = "u",
                LockExpiresAt = DateTime.UtcNow.AddMinutes(10),
                ConcurrencyVersion = 5
            };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(response);

            var ex = await Assert.ThrowsAsync<ConcurrencyConflictException>(
                () => _sut.SaveProgressAsync(id, "u", token, 3, new List<QuestionnaireResponseItem>()));

            Assert.Equal(3, ex.ExpectedVersion);
            Assert.Equal(5, ex.ActualVersion);
        }

        [Fact]
        public async Task SaveProgressAsync_NotInProgress_Throws()
        {
            var id = Guid.NewGuid();
            var response = new QuestionnaireResponse { Id = id, Status = ResponseStatus.Completed };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(response);

            await Assert.ThrowsAsync<BusinessRuleException>(
                () => _sut.SaveProgressAsync(id, "u", Guid.NewGuid(), null, new List<QuestionnaireResponseItem>()));
        }

        /// Cuestionario activo con una version publicada, que es lo minimo para responderlo.
        private static Questionnaire Published(Guid id, string fhirJson = null, Guid? versionId = null)
        {
            var vid = versionId ?? Guid.NewGuid();
            var q = new Questionnaire { Id = id, Status = QuestionnaireStatus.Active, CurrentVersionId = vid };
            q.Versions.Add(new QuestionnaireVersion
            {
                Id = vid,
                VersionNumber = 1,
                FhirJson = fhirJson ?? "{\"resourceType\":\"Questionnaire\",\"item\":[]}"
            });
            return q;
        }
    }
}
