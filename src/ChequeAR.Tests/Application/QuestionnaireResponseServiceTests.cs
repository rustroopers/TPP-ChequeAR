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
                _repo.Object, _qRepo.Object, _validator.Object, TestHelpers.NoopAuditDispatcher());
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

            await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.FinalizeAsync(id, "u", lockToken));

            Assert.Equal(ResponseStatus.InProgress, response.Status);
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

            await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CreateAsync(
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

            await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CreateAsync(
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

            await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.AcquireLockAsync(id, "u"));
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

            await Assert.ThrowsAsync<InvalidOperationException>(
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

            await Assert.ThrowsAsync<InvalidOperationException>(
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
