using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Exceptions;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Security;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ChequeAR.Tests.Application
{
    /// <summary>
    /// Escenarios de punta a punta del completado asincrónico, a nivel de servicio.
    ///
    /// QuestionnaireResponseServiceTests prueba cada operación aislada, con el repositorio
    /// mockeado llamada por llamada. Acá las operaciones se encadenan contra un repositorio
    /// en memoria que conserva el estado entre una y otra, que es como se usan de verdad:
    /// una ficha se crea, se bloquea, se guarda varias veces —a veces por médicos distintos,
    /// a veces con días de diferencia— y recién después se finaliza.
    /// </summary>
    public class QuestionnaireResponseSessionScenarioTests
    {
        private const string Structure =
            "{\"resourceType\":\"Questionnaire\",\"item\":[" +
            "{\"linkId\":\"peso\",\"text\":\"Peso (kg)\",\"type\":\"decimal\"}," +
            "{\"linkId\":\"presion\",\"text\":\"Presión arterial\",\"type\":\"string\"}," +
            "{\"linkId\":\"sintomas\",\"text\":\"Síntomas\",\"type\":\"coding\",\"repeats\":true,\"answerOption\":[{\"valueCoding\":{\"code\":\"Dolor\"}},{\"valueCoding\":{\"code\":\"Fatiga\"}},{\"valueCoding\":{\"code\":\"Fiebre\"}}]}," +
            "{\"linkId\":\"estudio\",\"text\":\"Estudio adjunto\",\"type\":\"attachment\"}]}";

        private readonly InMemoryResponseRepository _repo = new InMemoryResponseRepository();
        private readonly Mock<IQuestionnaireRepository> _qRepo = new Mock<IQuestionnaireRepository>();
        private readonly Mock<IFhirValidator> _validator = new Mock<IFhirValidator>();
        private readonly Mock<IAttachmentStore> _attachments = new Mock<IAttachmentStore>();
        private readonly Guid _questionnaireId = Guid.NewGuid();
        private readonly QuestionnaireResponseService _sut;

        public QuestionnaireResponseSessionScenarioTests()
        {
            var versionId = Guid.NewGuid();
            var q = new Questionnaire { Id = _questionnaireId, Status = QuestionnaireStatus.Active, CurrentVersionId = versionId };
            q.Versions.Add(new QuestionnaireVersion { Id = versionId, VersionNumber = 1, FhirJson = Structure });
            _qRepo.Setup(r => r.GetAsync(_questionnaireId)).ReturnsAsync(q);
            _validator.Setup(v => v.Validate(It.IsAny<string>(), It.IsAny<string>())).Returns(TestHelpers.ValidOk());

            _sut = new QuestionnaireResponseService(
                _repo, _qRepo.Object, _validator.Object, TestHelpers.NoopAuditDispatcher(), TestHelpers.UnrestrictedGuard(), TestHelpers.UnrestrictedAccess(), _attachments.Object);
        }

        // Ficha nueva desde cero: no hay borrador previo, se crea vacía y se va llenando
        // con los autosaves hasta finalizar.
        [Fact]
        public async Task NewResponse_FullCycle_CreateLockSaveFinalize()
        {
            var created = await NewResponseAsync("medico-a");
            var locked = await _sut.AcquireLockAsync(created.Id, "medico-a");
            var token = locked.LockToken.Value;

            var first = await _sut.SaveProgressAsync(created.Id, "medico-a", token, locked.ConcurrencyVersion, Items(Decimal("peso", 32.5m)));
            await _sut.SaveProgressAsync(created.Id, "medico-a", token, first.ConcurrencyVersion, Items(Text("presion", "100/60")));
            var finalized = await _sut.FinalizeAsync(created.Id, "medico-a", token);

            Assert.Equal(ResponseStatus.Completed, finalized.Status);
            Assert.Equal(new[] { "peso", "presion" }, finalized.Items.Select(i => i.LinkId).OrderBy(l => l));
            Assert.Null(finalized.LockToken);
            var fhir = JObject.Parse(finalized.FhirJson);
            Assert.Equal("completed", (string)fhir["status"]);
            Assert.Equal(new[] { "peso", "presion" }, fhir["item"].Select(i => (string)i["linkId"]).OrderBy(l => l));
        }

        // El caso que motivó la feature: el seguimiento empieza un médico en el consultorio
        // y lo continúa otro en una llamada posterior. Lo que respondió el primero no se
        // pierde y el segundo no necesita reenviarlo.
        [Fact]
        public async Task FollowUp_AcrossSessionsAndUsers_AccumulatesAnswers()
        {
            var created = await NewResponseAsync("medico-a");

            var sessionA = await _sut.AcquireLockAsync(created.Id, "medico-a");
            await _sut.SaveProgressAsync(created.Id, "medico-a", sessionA.LockToken.Value, null, Items(Decimal("peso", 32.5m)));
            await _sut.ReleaseLockAsync(created.Id, "medico-a", sessionA.LockToken.Value);

            var sessionB = await _sut.AcquireLockAsync(created.Id, "medico-b");
            var saved = await _sut.SaveProgressAsync(created.Id, "medico-b", sessionB.LockToken.Value, null, Items(Text("presion", "100/60")));

            Assert.Equal(ResponseStatus.InProgress, saved.Status);
            Assert.Equal(32.5m, saved.Items.Single(i => i.LinkId == "peso").ValueDecimal);
            Assert.Equal("100/60", saved.Items.Single(i => i.LinkId == "presion").ValueString);
            Assert.Equal("medico-b", saved.LockedByUserId);
        }

        // Dos médicos sobre la misma ficha: mientras el primero tiene la sesión abierta, el
        // segundo no puede tomarla ni escribir por ningún camino.
        [Fact]
        public async Task TwoUsers_SecondCannotLockNorWriteWhileFirstHoldsTheLock()
        {
            var created = await NewResponseAsync("medico-a");
            var sessionA = await _sut.AcquireLockAsync(created.Id, "medico-a");
            await _sut.SaveProgressAsync(created.Id, "medico-a", sessionA.LockToken.Value, null, Items(Decimal("peso", 32.5m)));
            var intruderToken = Guid.NewGuid();

            var lockEx = await Assert.ThrowsAsync<LockConflictException>(() => _sut.AcquireLockAsync(created.Id, "medico-b"));
            await Assert.ThrowsAsync<LockConflictException>(
                () => _sut.SaveProgressAsync(created.Id, "medico-b", intruderToken, null, Items(Decimal("peso", 99m))));
            await Assert.ThrowsAsync<LockConflictException>(() => _sut.FinalizeAsync(created.Id, "medico-b", intruderToken));
            await Assert.ThrowsAsync<LockConflictException>(() => _sut.AbandonAsync(created.Id, "medico-b"));

            Assert.Equal("medico-a", lockEx.LockedByUserId);
            var stored = await _repo.GetAsync(created.Id);
            Assert.Equal(ResponseStatus.InProgress, stored.Status);
            Assert.Equal(32.5m, stored.Items.Single(i => i.LinkId == "peso").ValueDecimal);
            Assert.Equal(sessionA.LockToken, stored.LockToken);
        }

        // Vencimiento con colisión: al primero se le venció el lock, otro lo tomó en el
        // medio, y el guardado que llega tarde se rechaza. Lo que el primero ya había
        // guardado sigue ahí.
        [Fact]
        public async Task ExpiredLock_TakenByAnotherUser_LateSaveIsRejectedAndEarlierWorkSurvives()
        {
            var created = await NewResponseAsync("medico-a");
            var sessionA = await _sut.AcquireLockAsync(created.Id, "medico-a");
            var tokenA = sessionA.LockToken.Value;
            await _sut.SaveProgressAsync(created.Id, "medico-a", tokenA, null, Items(Decimal("peso", 32.5m)));
            (await _repo.GetAsync(created.Id)).LockExpiresAt = DateTime.UtcNow.AddMinutes(-1);

            var sessionB = await _sut.AcquireLockAsync(created.Id, "medico-b");
            await Assert.ThrowsAsync<LockConflictException>(
                () => _sut.SaveProgressAsync(created.Id, "medico-a", tokenA, null, Items(Decimal("peso", 40m))));
            var saved = await _sut.SaveProgressAsync(created.Id, "medico-b", sessionB.LockToken.Value, null, Items(Text("presion", "100/60")));

            Assert.NotEqual(tokenA, sessionB.LockToken.Value);
            Assert.Equal(32.5m, saved.Items.Single(i => i.LinkId == "peso").ValueDecimal);
        }

        // Regresión del defecto encontrado probando en el navegador: $lock es una escritura
        // más e incrementa la versión. El cliente tiene que usar la versión que devuelve el
        // lock; con la que tenía antes de tomarlo, el primer autosave choca.
        [Fact]
        public async Task AcquireLock_BumpsVersion_FirstSaveMustUseTheVersionReturnedByTheLock()
        {
            var created = await NewResponseAsync("medico-a");
            var versionBeforeLock = created.ConcurrencyVersion;

            var locked = await _sut.AcquireLockAsync(created.Id, "medico-a");
            var versionAfterLock = locked.ConcurrencyVersion;
            var token = locked.LockToken.Value;

            Assert.Equal(versionBeforeLock + 1, versionAfterLock);
            await Assert.ThrowsAsync<ConcurrencyConflictException>(
                () => _sut.SaveProgressAsync(created.Id, "medico-a", token, versionBeforeLock, Items(Decimal("peso", 32.5m))));
            var saved = await _sut.SaveProgressAsync(created.Id, "medico-a", token, versionAfterLock, Items(Decimal("peso", 32.5m)));
            Assert.Equal(versionAfterLock + 1, saved.ConcurrencyVersion);
        }

        // Dos guardados que parten de la misma versión (dos pestañas del mismo médico): el
        // primero entra y el segundo se rechaza en vez de pisarlo.
        [Fact]
        public async Task TwoSavesFromTheSameVersion_SecondIsRejected()
        {
            var created = await NewResponseAsync("medico-a");
            var locked = await _sut.AcquireLockAsync(created.Id, "medico-a");
            var token = locked.LockToken.Value;
            var version = locked.ConcurrencyVersion;

            await _sut.SaveProgressAsync(created.Id, "medico-a", token, version, Items(Decimal("peso", 32.5m)));
            var ex = await Assert.ThrowsAsync<ConcurrencyConflictException>(
                () => _sut.SaveProgressAsync(created.Id, "medico-a", token, version, Items(Decimal("peso", 40m))));

            Assert.Equal(version, ex.ExpectedVersion);
            Assert.Equal(version + 1, ex.ActualVersion);
            Assert.Equal(32.5m, (await _repo.GetAsync(created.Id)).Items.Single(i => i.LinkId == "peso").ValueDecimal);
        }

        // Opción múltiple: cada opción marcada es una fila con el mismo linkId. Reguardar la
        // pregunta reemplaza el conjunto entero; si se acumulara, desmarcar una opción no
        // tendría efecto.
        [Fact]
        public async Task MultipleChoice_ResavingReplacesTheWholeSelection()
        {
            var created = await NewResponseAsync("medico-a");
            var token = (await _sut.AcquireLockAsync(created.Id, "medico-a")).LockToken.Value;
            await _sut.SaveProgressAsync(created.Id, "medico-a", token, null,
                Items(Decimal("peso", 32.5m), Code("sintomas", "Dolor"), Code("sintomas", "Fatiga")));

            var saved = await _sut.SaveProgressAsync(created.Id, "medico-a", token, null, Items(Code("sintomas", "Fiebre")));

            Assert.Equal(new[] { "Fiebre" }, saved.Items.Where(i => i.LinkId == "sintomas").Select(i => i.ValueCode));
            Assert.Equal(32.5m, saved.Items.Single(i => i.LinkId == "peso").ValueDecimal);
        }

        // Un adjunto cargado en una sesión sigue referenciado cuando la ficha se retoma y
        // se guarda otra pregunta.
        [Fact]
        public async Task Attachment_SurvivesLaterSessions()
        {
            var attachmentId = Subido("medico-a");
            var created = await NewResponseAsync("medico-a");
            var sessionA = await _sut.AcquireLockAsync(created.Id, "medico-a");
            await _sut.SaveProgressAsync(created.Id, "medico-a", sessionA.LockToken.Value, null,
                Items(new QuestionnaireResponseItem { LinkId = "estudio", AttachmentId = attachmentId }));
            await _sut.ReleaseLockAsync(created.Id, "medico-a", sessionA.LockToken.Value);

            var sessionB = await _sut.AcquireLockAsync(created.Id, "medico-a");
            var saved = await _sut.SaveProgressAsync(created.Id, "medico-a", sessionB.LockToken.Value, null, Items(Decimal("peso", 32.5m)));

            Assert.Equal(attachmentId, saved.Items.Single(i => i.LinkId == "estudio").AttachmentId);
        }

        private Guid Subido(string uploadedBy)
        {
            var id = Guid.NewGuid();
            _attachments.Setup(a => a.GetAsync(id)).ReturnsAsync(new Attachment { Id = id, UploadedBy = uploadedBy });
            return id;
        }

        [Fact]
        public async Task Attachment_DeOtroUsuario_NoSePuedeVincular()
        {
            var ajeno = Subido("medico-onco");
            var created = await NewResponseAsync("medico-a");
            var session = await _sut.AcquireLockAsync(created.Id, "medico-a");

            await Assert.ThrowsAsync<ValidationFailedException>(() => _sut.SaveProgressAsync(created.Id, "medico-a", session.LockToken.Value, null,
                Items(new QuestionnaireResponseItem { LinkId = "estudio", AttachmentId = ajeno })));
            await Assert.ThrowsAsync<ValidationFailedException>(() => _sut.SaveProgressAsync(created.Id, "medico-a", session.LockToken.Value, null,
                Items(new QuestionnaireResponseItem { LinkId = "estudio", AttachmentId = Guid.NewGuid() })));   // inexistente
            Assert.DoesNotContain((await _repo.GetAsync(created.Id)).Items, i => i.AttachmentId.HasValue);
        }

        [Fact]
        public async Task Attachment_QueYaEstabaEnLaFicha_OtroMedicoLoConserva()
        {
            var deA = Subido("medico-a");
            var created = await NewResponseAsync("medico-a");
            var sessionA = await _sut.AcquireLockAsync(created.Id, "medico-a");
            await _sut.SaveProgressAsync(created.Id, "medico-a", sessionA.LockToken.Value, null,
                Items(new QuestionnaireResponseItem { LinkId = "estudio", AttachmentId = deA }));
            await _sut.ReleaseLockAsync(created.Id, "medico-a", sessionA.LockToken.Value);

            var sessionB = await _sut.AcquireLockAsync(created.Id, "medico-b");
            var saved = await _sut.SaveProgressAsync(created.Id, "medico-b", sessionB.LockToken.Value, null,
                Items(new QuestionnaireResponseItem { LinkId = "estudio", AttachmentId = deA }));

            Assert.Equal(deA, saved.Items.Single(i => i.LinkId == "estudio").AttachmentId);
        }

        // Una ficha finalizada queda cerrada: no se puede volver a bloquear, guardar ni
        // descartar. Finalizarla de nuevo no es un error, pero tampoco la modifica.
        [Fact]
        public async Task CompletedResponse_RejectsFurtherWrites()
        {
            var created = await NewResponseAsync("medico-a");
            var token = (await _sut.AcquireLockAsync(created.Id, "medico-a")).LockToken.Value;
            await _sut.SaveProgressAsync(created.Id, "medico-a", token, null, Items(Decimal("peso", 32.5m)));
            var finalized = await _sut.FinalizeAsync(created.Id, "medico-a", token);
            var versionAtFinalize = finalized.ConcurrencyVersion;

            await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.AcquireLockAsync(created.Id, "medico-b"));
            await Assert.ThrowsAsync<BusinessRuleException>(
                () => _sut.SaveProgressAsync(created.Id, "medico-a", token, null, Items(Decimal("peso", 40m))));
            await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.AbandonAsync(created.Id, "medico-a"));
            var again = await _sut.FinalizeAsync(created.Id, "medico-b", Guid.NewGuid());

            Assert.Equal(ResponseStatus.Completed, again.Status);
            Assert.Equal(versionAtFinalize, again.ConcurrencyVersion);
            Assert.Equal(32.5m, again.Items.Single(i => i.LinkId == "peso").ValueDecimal);
        }

        // Un borrador descartado no se puede retomar: para seguir hay que empezar otro.
        [Fact]
        public async Task AbandonedResponse_CannotBeResumed()
        {
            var created = await NewResponseAsync("medico-a");

            var abandoned = await _sut.AbandonAsync(created.Id, "medico-a");

            Assert.Equal(ResponseStatus.EnteredInError, abandoned.Status);
            await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.AcquireLockAsync(created.Id, "medico-a"));
        }

        // Después de "Guardar avance" la sesión quedó cerrada: un autosave rezagado de esa
        // misma pestaña ya no entra, hay que volver a tomar el lock.
        [Fact]
        public async Task SaveAfterRelease_RequiresTakingTheLockAgain()
        {
            var created = await NewResponseAsync("medico-a");
            var token = (await _sut.AcquireLockAsync(created.Id, "medico-a")).LockToken.Value;
            await _sut.ReleaseLockAsync(created.Id, "medico-a", token);

            await Assert.ThrowsAsync<BusinessRuleException>(
                () => _sut.SaveProgressAsync(created.Id, "medico-a", token, null, Items(Decimal("peso", 32.5m))));
            await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.RenewLockAsync(created.Id, "medico-a", token));
        }

        [Fact]
        public async Task UnknownResponse_EveryWriteOperationThrows()
        {
            var id = Guid.NewGuid();
            var token = Guid.NewGuid();

            await Assert.ThrowsAsync<NotFoundException>(() => _sut.AcquireLockAsync(id, "u"));
            await Assert.ThrowsAsync<NotFoundException>(() => _sut.RenewLockAsync(id, "u", token));
            await Assert.ThrowsAsync<NotFoundException>(() => _sut.ReleaseLockAsync(id, "u", token));
            await Assert.ThrowsAsync<NotFoundException>(() => _sut.SaveProgressAsync(id, "u", token, null, Items()));
            await Assert.ThrowsAsync<NotFoundException>(() => _sut.FinalizeAsync(id, "u", token));
            await Assert.ThrowsAsync<NotFoundException>(() => _sut.AbandonAsync(id, "u"));
            Assert.Null(await _sut.GetAsync(id));
        }

        private Task<QuestionnaireResponse> NewResponseAsync(string userId)
            => _sut.CreateAsync(new QuestionnaireResponse { QuestionnaireId = _questionnaireId, PatientHospitalId = "PAC-000043" }, userId);

        private static List<QuestionnaireResponseItem> Items(params QuestionnaireResponseItem[] items) => items.ToList();

        private static QuestionnaireResponseItem Decimal(string linkId, decimal value)
            => new QuestionnaireResponseItem { LinkId = linkId, ValueDecimal = value };

        private static QuestionnaireResponseItem Text(string linkId, string value)
            => new QuestionnaireResponseItem { LinkId = linkId, ValueString = value };

        private static QuestionnaireResponseItem Code(string linkId, string value)
            => new QuestionnaireResponseItem { LinkId = linkId, ValueCode = value };

        /// <summary>
        /// Repositorio en memoria que respeta el contrato de IQuestionnaireResponseRepository:
        /// SaveProgressAsync reemplaza sólo los items de los linkId que llegan y RetargetAsync
        /// conserva sólo los linkId indicados. Devuelve siempre la misma instancia, como una
        /// entidad trackeada por EF.
        /// </summary>
        private sealed class InMemoryResponseRepository : IQuestionnaireResponseRepository
        {
            private readonly Dictionary<Guid, QuestionnaireResponse> _store = new Dictionary<Guid, QuestionnaireResponse>();

            public Task<QuestionnaireResponse> GetAsync(Guid id)
                => Task.FromResult(_store.TryGetValue(id, out var r) ? r : null);

            public Task<IReadOnlyList<QuestionnaireResponse>> SearchAsync(
                string patientHospitalId, Guid? questionnaireId, ResponseStatus? status, int skip, int take, SpecialtyScope scope = null)
            {
                IReadOnlyList<QuestionnaireResponse> found = _store.Values
                    .Where(r => patientHospitalId == null || r.PatientHospitalId == patientHospitalId)
                    .Where(r => !questionnaireId.HasValue || r.QuestionnaireId == questionnaireId.Value)
                    .Where(r => !status.HasValue || r.Status == status.Value)
                    .Skip(skip).Take(take).ToList();
                return Task.FromResult(found);
            }

            public Task AddAsync(QuestionnaireResponse r)
            {
                _store[r.Id] = r;
                return Task.CompletedTask;
            }

            public Task UpdateAsync(QuestionnaireResponse r) => Task.CompletedTask;

            public Task AddItemsAsync(IEnumerable<QuestionnaireResponseItem> items)
            {
                foreach (var item in items) _store[item.ResponseId].Items.Add(item);
                return Task.CompletedTask;
            }

            public Task SaveProgressAsync(QuestionnaireResponse r, ICollection<QuestionnaireResponseItem> replacementItems)
            {
                var touched = new HashSet<string>(replacementItems.Select(i => i.LinkId));
                r.Items = r.Items.Where(i => !touched.Contains(i.LinkId)).Concat(replacementItems).ToList();
                return Task.CompletedTask;
            }

            public Task RetargetAsync(QuestionnaireResponse r, ICollection<string> keepLinkIds)
            {
                r.Items = r.Items.Where(i => keepLinkIds.Contains(i.LinkId)).ToList();
                return Task.CompletedTask;
            }

            public Task<IReadOnlyList<Guid>> FindQuestionnaireIdsByAttachmentAsync(Guid attachmentId)
                => Task.FromResult<IReadOnlyList<Guid>>(_store.Values
                    .Where(r => (r.Items ?? new List<QuestionnaireResponseItem>()).Any(i => i.AttachmentId == attachmentId))
                    .Select(r => r.QuestionnaireId).Distinct().ToList());

            public Task<bool> HasResponsesAsync(Guid questionnaireId)
                => Task.FromResult(_store.Values.Any(r => r.QuestionnaireId == questionnaireId));

            public Task<int> CountAsync(string patientHospitalId = null, Guid? questionnaireId = null, ResponseStatus? status = null, SpecialtyScope scope = null)
                => Task.FromResult(_store.Values.Count(r =>
                    (patientHospitalId == null || r.PatientHospitalId == patientHospitalId)
                    && (!questionnaireId.HasValue || r.QuestionnaireId == questionnaireId.Value)
                    && (!status.HasValue || r.Status == status.Value)));
        }
    }
}
