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
using Xunit;

namespace ChequeAR.Tests.Application
{
    /// <summary>
    /// Integridad de lo que se guarda y se publica: linkId únicos, condiciones que se pueden
    /// cumplir, concurrencia al publicar y snapshot de las variables.
    /// </summary>
    public class QuestionnaireServiceIntegrityTests
    {
        private readonly Mock<IQuestionnaireRepository> _repo = new Mock<IQuestionnaireRepository>();
        private readonly Mock<IQuestionnaireResponseRepository> _responses = new Mock<IQuestionnaireResponseRepository>();
        private readonly Mock<IFhirValidator> _validator = new Mock<IFhirValidator>();

        public QuestionnaireServiceIntegrityTests()
        {
            _validator.Setup(v => v.Validate(It.IsAny<string>(), It.IsAny<string>())).Returns(TestHelpers.ValidOk());
        }

        private QuestionnaireService Sut(params ClinicalVariable[] variables)
            => new QuestionnaireService(_repo.Object, _responses.Object, _validator.Object,
                TestHelpers.NoopAuditDispatcher(), TestHelpers.UnrestrictedGuard(), TestHelpers.CatalogWith(),
                TestHelpers.UnrestrictedAccess(), TestHelpers.VariableRepoWith(variables));

        private Questionnaire Persistido(params Question[] preguntas)
        {
            var q = new Questionnaire
            {
                Id = Guid.NewGuid(), Title = "Control", SpecialtyCode = "cardio",
                Status = QuestionnaireStatus.Draft, ConcurrencyVersion = 3
            };
            foreach (var p in preguntas) { p.Id = Guid.NewGuid(); q.Questions.Add(p); }
            _repo.Setup(r => r.GetAsync(q.Id)).ReturnsAsync(q);
            return q;
        }

        private static ClinicalVariable Tabaquismo(int revision, string fumador)
        {
            var v = TestHelpers.Variable("tabaco", QuestionType.Choice, revision: revision);
            v.Options = new List<VariableOption>
            {
                new VariableOption { Code = "77176002", SnomedCode = "77176002", Value = fumador, Display = fumador, OrderIndex = 0 },
                new VariableOption { Code = "8392000", SnomedCode = "8392000", Value = "No fumador", Display = "No fumador", OrderIndex = 1 }
            };
            return v;
        }

        // ── linkId únicos ─────────────────────────────────────────────────────────────

        // Dos preguntas con el mismo linkId comparten respuestas y columna de reporte: no se
        // guarda ni siquiera como borrador.
        [Fact]
        public async Task CreateAsync_LinkIdRepetido_Falla()
        {
            var peso = TestHelpers.Variable("peso");
            var q = new Questionnaire { Title = "Control", SpecialtyCode = "cardio" };
            q.Questions.Add(TestHelpers.QuestionFrom(peso, "peso-2"));
            q.Questions.Add(TestHelpers.QuestionFrom(peso, "peso-2"));

            var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => Sut(peso).CreateAsync(q, "jefe"));

            Assert.Contains(ex.Errors, e => e.Contains("peso-2"));
            _repo.Verify(r => r.AddAsync(It.IsAny<Questionnaire>()), Times.Never);
        }

        [Fact]
        public async Task UpdateAsync_LinkIdRepetidoEntreSecciones_Falla()
        {
            var peso = TestHelpers.Variable("peso");
            var q = Persistido(TestHelpers.QuestionFrom(peso));

            var patch = new Questionnaire();
            var a = new QuestionGroup { LinkId = "seccion-1", Title = "A" };
            a.Questions.Add(TestHelpers.QuestionFrom(peso, "peso-1"));
            var b = new QuestionGroup { LinkId = "seccion-2", Title = "B" };
            b.Questions.Add(TestHelpers.QuestionFrom(peso, "peso-1"));
            patch.Groups.Add(a);
            patch.Groups.Add(b);

            await Assert.ThrowsAsync<ValidationFailedException>(() => Sut(peso).UpdateAsync(q.Id, patch, "jefe"));
            _repo.Verify(r => r.UpdateWithStructureAsync(It.IsAny<Questionnaire>(),
                It.IsAny<ICollection<QuestionGroup>>(), It.IsAny<ICollection<Question>>()), Times.Never);
        }

        // ── Snapshot de la variable ──────────────────────────────────────────────────

        // Guardar no actualiza en silencio la definición de una pregunta que ya existía: si el
        // Administrador renombró una opción, la pregunta conserva la que tenía y la condición
        // que apunta a ella sigue cumpliéndose. Tomar la definición nueva es una decisión
        // explícita (quitar y volver a agregar).
        [Fact]
        public async Task UpdateAsync_PreguntaExistente_ConservaElSnapshotAunqueLaVariableCambie()
        {
            var v1 = Tabaquismo(revision: 1, fumador: "Fumador");
            var pregunta = TestHelpers.QuestionFrom(v1, "tabaco-1");
            ClinicalVariableSnapshot.Apply(pregunta, v1);
            var q = Persistido(pregunta);

            var v2 = Tabaquismo(revision: 2, fumador: "Fumador actual");
            v2.Id = v1.Id;

            var patch = new Questionnaire();
            patch.Questions.Add(TestHelpers.QuestionFrom(v2, "tabaco-1", "¿Fuma?"));
            ICollection<Question> guardadas = null;
            _repo.Setup(r => r.UpdateWithStructureAsync(It.IsAny<Questionnaire>(), It.IsAny<ICollection<QuestionGroup>>(), It.IsAny<ICollection<Question>>()))
                 .Callback<Questionnaire, ICollection<QuestionGroup>, ICollection<Question>>((_, __, qs) => guardadas = qs)
                 .Returns(Task.CompletedTask);

            await Sut(v2).UpdateAsync(q.Id, patch, "jefe");

            var guardada = guardadas.Single();
            Assert.Equal(1, guardada.VariableRevision);
            Assert.Equal("Fumador", guardada.Options.OrderBy(o => o.OrderIndex).First().Display);
            Assert.Equal("¿Fuma?", guardada.Text);   // el enunciado sí es del cuestionario
        }

        // Una pregunta NUEVA toma la definición vigente de la variable.
        [Fact]
        public async Task UpdateAsync_PreguntaNueva_TomaLaRevisionVigente()
        {
            var v1 = Tabaquismo(revision: 1, fumador: "Fumador");
            var existente = TestHelpers.QuestionFrom(v1, "tabaco-1");
            ClinicalVariableSnapshot.Apply(existente, v1);
            var q = Persistido(existente);

            var v2 = Tabaquismo(revision: 2, fumador: "Fumador actual");
            v2.Id = v1.Id;

            var patch = new Questionnaire();
            patch.Questions.Add(TestHelpers.QuestionFrom(v2, "tabaco-1"));
            patch.Questions.Add(TestHelpers.QuestionFrom(v2, "tabaco-2"));
            ICollection<Question> guardadas = null;
            _repo.Setup(r => r.UpdateWithStructureAsync(It.IsAny<Questionnaire>(), It.IsAny<ICollection<QuestionGroup>>(), It.IsAny<ICollection<Question>>()))
                 .Callback<Questionnaire, ICollection<QuestionGroup>, ICollection<Question>>((_, __, qs) => guardadas = qs)
                 .Returns(Task.CompletedTask);

            await Sut(v2).UpdateAsync(q.Id, patch, "jefe");

            var nueva = guardadas.Single(x => x.LinkId == "tabaco-2");
            Assert.Equal(2, nueva.VariableRevision);
            Assert.Equal("Fumador actual", nueva.Options.OrderBy(o => o.OrderIndex).First().Display);
        }

        // ── Publicación ──────────────────────────────────────────────────────────────

        // Publicar congela lo que hay en la base: si alguien guardó después de que quien
        // publica lo vio, se rechaza en vez de congelar cambios que nunca revisó.
        [Fact]
        public async Task PublishAsync_VersionDesactualizada_LanzaConflicto()
        {
            var peso = TestHelpers.Variable("peso");
            var q = Persistido(TestHelpers.QuestionFrom(peso));

            await Assert.ThrowsAsync<ConcurrencyConflictException>(
                () => Sut(peso).PublishAsync(q.Id, "jefe", expectedVersion: 2));
            _repo.Verify(r => r.PublishAsync(It.IsAny<Questionnaire>(), It.IsAny<QuestionnaireVersion>()), Times.Never);
        }

        [Fact]
        public async Task PublishAsync_VersionAlDia_Publica()
        {
            var peso = TestHelpers.Variable("peso");
            var q = Persistido(TestHelpers.QuestionFrom(peso));

            await Sut(peso).PublishAsync(q.Id, "jefe", expectedVersion: 3);

            _repo.Verify(r => r.PublishAsync(q, It.IsAny<QuestionnaireVersion>()), Times.Once);
        }

        // Reactivar un archivado que no cambió no fabrica una versión idéntica a la anterior.
        [Fact]
        public async Task PublishAsync_ArchivadoSinCambios_SeReactivaSinVersionNueva()
        {
            var peso = TestHelpers.Variable("peso");
            var q = Persistido(TestHelpers.QuestionFrom(peso));
            var v1 = new QuestionnaireVersion { Id = Guid.NewGuid(), QuestionnaireId = q.Id, VersionNumber = 1 };
            q.Versions.Add(v1);
            q.CurrentVersionId = v1.Id;
            q.Status = QuestionnaireStatus.Retired;
            q.HasPendingChanges = false;

            var result = await Sut(peso).PublishAsync(q.Id, "jefe");

            Assert.Equal(QuestionnaireStatus.Active, result.Status);
            Assert.Equal(v1.Id, result.CurrentVersionId);
            _repo.Verify(r => r.PublishAsync(It.IsAny<Questionnaire>(), It.IsAny<QuestionnaireVersion>()), Times.Never);
            _repo.Verify(r => r.UpdateAsync(q), Times.Once);
        }

        // Pero si se archivó con cambios pendientes, publicarlo sí congela esos cambios.
        [Fact]
        public async Task PublishAsync_ArchivadoConCambiosPendientes_CongelaVersionNueva()
        {
            var peso = TestHelpers.Variable("peso");
            var q = Persistido(TestHelpers.QuestionFrom(peso));
            var v1 = new QuestionnaireVersion { Id = Guid.NewGuid(), QuestionnaireId = q.Id, VersionNumber = 1 };
            q.Versions.Add(v1);
            q.CurrentVersionId = v1.Id;
            q.Status = QuestionnaireStatus.Retired;
            q.HasPendingChanges = true;

            await Sut(peso).PublishAsync(q.Id, "jefe");

            _repo.Verify(r => r.PublishAsync(q, It.Is<QuestionnaireVersion>(v => v.VersionNumber == 2)), Times.Once);
        }

        // Una condición sin valor es normal mientras se edita, pero publicada es una pregunta
        // que nadie va a ver nunca.
        [Fact]
        public async Task PublishAsync_CondicionSinValor_Falla()
        {
            var fuma = TestHelpers.Variable("fuma", QuestionType.Boolean);
            var cuanto = TestHelpers.Variable("cuanto", QuestionType.Integer);
            var dependiente = TestHelpers.QuestionFrom(cuanto);
            dependiente.Type = QuestionType.Integer;
            dependiente.EnableWhenJson = "{\"question\":\"fuma\",\"op\":\"=\",\"value\":\"\"}";
            var origen = TestHelpers.QuestionFrom(fuma);
            origen.Type = QuestionType.Boolean;
            var q = Persistido(origen, dependiente);

            var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => Sut(fuma, cuanto).PublishAsync(q.Id, "jefe"));

            Assert.Contains(ex.Errors, e => e.Contains("sin valor"));
        }

        [Fact]
        public async Task PublishAsync_CondicionSobreOpcionInexistente_Falla()
        {
            var tabaco = Tabaquismo(1, "Fumador");
            var cuanto = TestHelpers.Variable("cuanto", QuestionType.Integer);
            var origen = TestHelpers.QuestionFrom(tabaco, "tabaco-1");
            ClinicalVariableSnapshot.Apply(origen, tabaco);
            var dependiente = TestHelpers.QuestionFrom(cuanto);
            dependiente.Type = QuestionType.Integer;
            dependiente.EnableWhenJson = "{\"question\":\"tabaco-1\",\"op\":\"=\",\"value\":\"Ex fumador\"}";
            var q = Persistido(origen, dependiente);

            var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => Sut(tabaco, cuanto).PublishAsync(q.Id, "jefe"));

            Assert.Contains(ex.Errors, e => e.Contains("Ex fumador"));
        }

        [Fact]
        public async Task PublishAsync_CondicionSobreOpcionExistente_Publica()
        {
            var tabaco = Tabaquismo(1, "Fumador");
            var cuanto = TestHelpers.Variable("cuanto", QuestionType.Integer);
            var origen = TestHelpers.QuestionFrom(tabaco, "tabaco-1");
            ClinicalVariableSnapshot.Apply(origen, tabaco);
            var dependiente = TestHelpers.QuestionFrom(cuanto);
            dependiente.Type = QuestionType.Integer;
            dependiente.EnableWhenJson = "{\"question\":\"tabaco-1\",\"op\":\"=\",\"value\":\"Fumador\"}";
            var q = Persistido(origen, dependiente);

            await Sut(tabaco, cuanto).PublishAsync(q.Id, "jefe");

            _repo.Verify(r => r.PublishAsync(q, It.IsAny<QuestionnaireVersion>()), Times.Once);
        }

        // La entidad cargada por EF tiene las preguntas de una sección en las dos colecciones:
        // no se reportan dos veces.
        [Fact]
        public async Task PublishAsync_PreguntaEnDosColecciones_SeReportaUnaVez()
        {
            var q = Persistido();
            var huerfana = new Question { Id = Guid.NewGuid(), LinkId = "suelta", Text = "Suelta" };
            var g = new QuestionGroup { Id = Guid.NewGuid(), LinkId = "s1", Title = "S1" };
            huerfana.GroupId = g.Id;
            g.Questions.Add(huerfana);
            q.Groups.Add(g);
            q.Questions.Add(huerfana);

            var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => Sut().PublishAsync(q.Id, "jefe"));

            Assert.Single(ex.Errors);
        }
    }
}
