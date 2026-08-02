using System;
using System.Linq;
using ChequeAR.Application.Fhir;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using Xunit;

namespace ChequeAR.Tests.Application
{
    /// <summary>
    /// Ida y vuelta de la ficha clínica. Mismo criterio que FhirRoundTripTests: si el
    /// renderizador manda algo que el parser no reconstruye, se pierde una respuesta de un
    /// paciente. Acá el costo de una pérdida silenciosa es más alto que en el editor.
    /// </summary>
    public class FhirResponseRoundTripTests
    {
        [Fact]
        public void ValoresTipados_SobrevivenElCiclo()
        {
            var original = Ficha(
                new QuestionnaireResponseItem { LinkId = "peso", ValueDecimal = 3.25m },
                new QuestionnaireResponseItem { LinkId = "edad", ValueInteger = 7 },
                new QuestionnaireResponseItem { LinkId = "fuma", ValueBoolean = true },
                new QuestionnaireResponseItem { LinkId = "nota", ValueString = "sin novedad" },
                new QuestionnaireResponseItem { LinkId = "dolor", ValueCode = "leve" });

            var vuelta = Ciclo(original);

            Assert.Equal(3.25m, Uno(vuelta, "peso").ValueDecimal);
            Assert.Equal(7, Uno(vuelta, "edad").ValueInteger);
            Assert.True(Uno(vuelta, "fuma").ValueBoolean);
            Assert.Equal("sin novedad", Uno(vuelta, "nota").ValueString);
            Assert.Equal("leve", Uno(vuelta, "dolor").ValueCode);
        }

        [Fact]
        public void Fecha_SobreviveElCiclo()
        {
            var original = Ficha(new QuestionnaireResponseItem
            {
                LinkId = "cuando",
                ValueDate = new DateTime(2026, 3, 15, 9, 45, 0)
            });

            Assert.Equal(new DateTime(2026, 3, 15, 9, 45, 0), Uno(Ciclo(original), "cuando").ValueDate);
        }

        /// <summary>
        /// Opción múltiple: varios registros con el mismo linkId van a UN item con varias
        /// answer, y tienen que volver a ser varios registros. Si el ciclo los colapsa, el
        /// paciente pierde síntomas marcados.
        /// </summary>
        [Fact]
        public void OpcionMultiple_NoSeColapsa()
        {
            var original = Ficha(
                new QuestionnaireResponseItem { LinkId = "sintomas", ValueCode = "fiebre" },
                new QuestionnaireResponseItem { LinkId = "sintomas", ValueCode = "tos" },
                new QuestionnaireResponseItem { LinkId = "sintomas", ValueCode = "dolor" });

            var sintomas = Ciclo(original).Items.Where(i => i.LinkId == "sintomas").ToList();

            Assert.Equal(3, sintomas.Count);
            Assert.Equal(new[] { "fiebre", "tos", "dolor" }, sintomas.Select(s => s.ValueCode));
        }

        [Fact]
        public void Adjunto_ConservaIdYNombreDeArchivo()
        {
            var attachmentId = Guid.NewGuid();
            var original = Ficha(new QuestionnaireResponseItem
            {
                LinkId = "rx",
                AttachmentId = attachmentId,
                ValueString = "rx-torax.pdf"
            });

            var vuelta = Uno(Ciclo(original), "rx");

            Assert.Equal(attachmentId, vuelta.AttachmentId);
            Assert.Equal("rx-torax.pdf", vuelta.ValueString);
        }

        [Fact]
        public void PacienteYCuestionario_SobrevivenElCiclo()
        {
            var original = Ficha(new QuestionnaireResponseItem { LinkId = "x", ValueString = "y" });

            var vuelta = Ciclo(original);

            Assert.Equal(original.PatientHospitalId, vuelta.PatientHospitalId);
            Assert.Equal(original.QuestionnaireId, vuelta.QuestionnaireId);
        }

        /// <summary>
        /// El anclaje de versión es lo que garantiza que la ficha refleje las preguntas que el
        /// médico leyó. Si se pierde en el ciclo, el servicio cae a la versión vigente y una
        /// publicación en el medio reinterpreta la ficha.
        /// </summary>
        [Fact]
        public void AnclajeDeVersion_SobreviveElCiclo()
        {
            var original = Ficha(new QuestionnaireResponseItem { LinkId = "x", ValueString = "y" });

            Assert.Equal(original.QuestionnaireVersionId, Ciclo(original).QuestionnaireVersionId);
        }

        [Fact]
        public void PreguntaSinResponder_SobreviveComoItemVacio()
        {
            var original = Ficha(new QuestionnaireResponseItem { LinkId = "vacia" });

            var vuelta = Uno(Ciclo(original), "vacia");

            Assert.Null(vuelta.ValueString);
            Assert.Null(vuelta.ValueCode);
            Assert.Null(vuelta.ValueBoolean);
        }

        // ---- helpers ----

        private static QuestionnaireResponse Ficha(params QuestionnaireResponseItem[] items)
        {
            var r = new QuestionnaireResponse
            {
                Id = Guid.NewGuid(),
                QuestionnaireId = Guid.NewGuid(),
                QuestionnaireVersionId = Guid.NewGuid(),
                PatientHospitalId = "PAC-000042",
                AuthorUserId = "dr.gomez",
                AuthoredAt = new DateTime(2026, 8, 1, 10, 30, 0, DateTimeKind.Utc),
                Status = ResponseStatus.InProgress
            };
            foreach (var i in items) r.Items.Add(i);
            return r;
        }

        private static QuestionnaireResponse Ciclo(QuestionnaireResponse r)
        {
            var json = FhirQuestionnaireResponseBuilder.Build(r, null);
            var recurso = FhirResourceReader.ReadResponse(json);
            Assert.NotNull(recurso);
            return FhirQuestionnaireResponseParser.ToDomain(recurso);
        }

        private static QuestionnaireResponseItem Uno(QuestionnaireResponse r, string linkId)
            => r.Items.Single(i => i.LinkId == linkId);
    }
}
