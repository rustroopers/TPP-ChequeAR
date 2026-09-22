using System;
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
    /// La regla central de la feature: un cuestionario ya no se escribe, se arma eligiendo
    /// variables del catálogo. Toda pregunta tiene que provenir de una variable existente y
    /// activa, y lo que la variable define gana sobre lo que mande el cliente.
    /// </summary>
    public class QuestionnaireServiceVariableTests
    {
        private readonly Mock<IQuestionnaireRepository> _repo = new Mock<IQuestionnaireRepository>();
        private readonly Mock<IQuestionnaireResponseRepository> _responses = new Mock<IQuestionnaireResponseRepository>();
        private readonly Mock<IFhirValidator> _validator = new Mock<IFhirValidator>();

        private QuestionnaireService Sut(params ClinicalVariable[] variables)
            => new QuestionnaireService(_repo.Object, _responses.Object, _validator.Object,
                TestHelpers.NoopAuditDispatcher(), TestHelpers.UnrestrictedGuard(), TestHelpers.CatalogWith(),
                TestHelpers.UnrestrictedAccess(), TestHelpers.VariableRepoWith(variables));

        private static Questionnaire Con(params Question[] preguntas)
        {
            var q = new Questionnaire { Title = "Control", SpecialtyCode = "cardio" };
            foreach (var p in preguntas) q.Questions.Add(p);
            return q;
        }

        // Es el corazón del cambio: una pregunta escrita a mano ya no se puede guardar, porque
        // no hay forma de decir qué concepto clínico mide ni de compararla con nada.
        [Fact]
        public async Task CreateAsync_PreguntaSinVariable_Falla()
        {
            var q = Con(new Question { LinkId = "suelta", Text = "¿Cómo se siente?" });

            var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => Sut().CreateAsync(q, "jefe"));

            Assert.Contains(ex.Errors, e => e.Contains("suelta"));
            _repo.Verify(r => r.AddAsync(It.IsAny<Questionnaire>()), Times.Never);
        }

        // Un id inventado por el cliente no debe convertirse en una FK rota en Oracle.
        [Fact]
        public async Task CreateAsync_VariableInexistente_Falla()
        {
            var q = Con(new Question { LinkId = "x", VariableId = Guid.NewGuid() });

            await Assert.ThrowsAsync<ValidationFailedException>(() => Sut().CreateAsync(q, "jefe"));
        }

        // Dar de baja una variable tiene que impedir su uso NUEVO: si no, la baja no significaría
        // nada y el catálogo seguiría creciendo con conceptos que el Administrador ya retiró.
        [Fact]
        public async Task CreateAsync_VariableInactiva_Falla()
        {
            var vieja = TestHelpers.Variable("peso-viejo", isActive: false);
            var q = Con(TestHelpers.QuestionFrom(vieja));

            var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => Sut(vieja).CreateAsync(q, "jefe"));

            Assert.Contains(ex.Errors, e => e.Contains("inactiva"));
        }

        // Pero un borrador que YA la usaba se tiene que poder seguir guardando: que el
        // Administrador retire una variable no puede romperle el trabajo en curso a un jefe.
        [Fact]
        public async Task UpdateAsync_VariableInactivaQueYaEstabaEnUso_SeAcepta()
        {
            var id = Guid.NewGuid();
            var vieja = TestHelpers.Variable("peso-viejo", isActive: false);
            var persistido = new Questionnaire { Id = id, Title = "Control", Status = QuestionnaireStatus.Draft, SpecialtyCode = "cardio" };
            persistido.Questions.Add(TestHelpers.QuestionFrom(vieja));
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(persistido);

            var patch = new Questionnaire { Title = "Control v2" };
            patch.Questions.Add(TestHelpers.QuestionFrom(vieja, "peso-viejo", "Peso al ingreso"));

            var actualizado = await Sut(vieja).UpdateAsync(id, patch, "jefe");

            Assert.NotNull(actualizado);
            _repo.Verify(r => r.UpdateWithStructureAsync(It.IsAny<Questionnaire>(),
                It.IsAny<System.Collections.Generic.ICollection<QuestionGroup>>(),
                It.IsAny<System.Collections.Generic.ICollection<Question>>()), Times.Once);
        }

        // El cliente no puede contradecir al catálogo. Bloquear el selector de tipo en la
        // interfaz es comodidad; esto es lo que lo hace cierto.
        [Fact]
        public async Task CreateAsync_ClienteMandaOtroTipo_GanaLaVariable()
        {
            var peso = TestHelpers.Variable("peso", QuestionType.Decimal);
            peso.Unit = "kg";
            var pregunta = TestHelpers.QuestionFrom(peso);
            pregunta.Type = QuestionType.Text;   // el cliente miente
            var q = Con(pregunta);

            var creado = await Sut(peso).CreateAsync(q, "jefe");

            var guardada = creado.Questions.First();
            Assert.Equal(QuestionType.Decimal, guardada.Type);
            Assert.Equal("kg", QuestionMetadataReader.ReadValidation(guardada.ValidationJson).Unit);
        }

        // El enunciado sí es del cuestionario: la misma variable puede ser "Peso al nacer" en
        // uno y "Peso al día de hoy" en otro. Es el único grado de libertad que se conserva.
        [Fact]
        public async Task CreateAsync_ConservaElEnunciadoDelCuestionario()
        {
            var peso = TestHelpers.Variable("peso");
            var q = Con(TestHelpers.QuestionFrom(peso, "peso-1", "Peso al nacer"));

            var creado = await Sut(peso).CreateAsync(q, "jefe");

            Assert.Equal("Peso al nacer", creado.Questions.First().Text);
        }

        // La misma variable dos veces en un cuestionario es un caso real ("Peso al ingreso" /
        // "Peso al alta"): lo que las distingue es el LinkId, no la variable.
        [Fact]
        public async Task CreateAsync_MismaVariableDosVeces_SeAceptaConLinkIdsDistintos()
        {
            var peso = TestHelpers.Variable("peso");
            var q = Con(
                TestHelpers.QuestionFrom(peso, "peso-1", "Peso al ingreso"),
                TestHelpers.QuestionFrom(peso, "peso-2", "Peso al alta"));

            var creado = await Sut(peso).CreateAsync(q, "jefe");

            Assert.Equal(2, creado.Questions.Count);
            Assert.Equal(new[] { "peso-1", "peso-2" }, creado.Questions.Select(x => x.LinkId).ToArray());
            Assert.All(creado.Questions, x => Assert.Equal(peso.Id, x.VariableId));
        }

        // Las preguntas dentro de una sección pasan por la misma regla: si sólo se validaran
        // las sueltas, cualquiera podría colar una pregunta a mano metiéndola en un grupo.
        [Fact]
        public async Task CreateAsync_PreguntaSinVariableDentroDeUnaSeccion_Falla()
        {
            var q = new Questionnaire { Title = "Control", SpecialtyCode = "cardio" };
            q.Groups.Add(new QuestionGroup
            {
                Title = "Sección",
                Questions = { new Question { LinkId = "escondida", Text = "¿?" } }
            });

            var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => Sut().CreateAsync(q, "jefe"));

            Assert.Contains(ex.Errors, e => e.Contains("escondida"));
        }

        // El clon copia la revisión del ORIGEN, no la vigente: nace idéntico a lo que se clonó.
        // Si tomara la vigente, clonar sería una forma encubierta de actualizar las variables.
        [Fact]
        public async Task CloneAsync_CopiaVariableIdYRevision()
        {
            var id = Guid.NewGuid();
            var peso = TestHelpers.Variable("peso", revision: 7);
            var origen = new Questionnaire { Id = id, Title = "Control", Status = QuestionnaireStatus.Active, SpecialtyCode = "cardio" };
            var pregunta = TestHelpers.QuestionFrom(peso);
            pregunta.VariableRevision = 2;   // se armó con una revisión anterior
            origen.Questions.Add(pregunta);
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(origen);

            var copia = await Sut(peso).CloneAsync(id, "Control (copia)", "jefe");

            var copiada = copia.Questions.First();
            Assert.Equal(peso.Id, copiada.VariableId);
            Assert.Equal(2, copiada.VariableRevision);
        }
    }
}
