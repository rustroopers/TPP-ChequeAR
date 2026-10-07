using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using ChequeAR.Application.Fhir;
using Hl7.Fhir.Model;
using Hl7.Fhir.Validation;
using Xunit;
using DomainAuditEvent = ChequeAR.Domain.Entities.AuditEvent;
using FhirAuditEvent = Hl7.Fhir.Model.AuditEvent;

namespace ChequeAR.Tests.Application
{
    /// <summary>
    /// R5 exige en AuditEvent: code, recorded, al menos un agent con who, y source.observer.
    /// Un recurso al que le falte alguno no valida y un consumidor externo lo rechaza.
    /// </summary>
    public class FhirAuditEventBuilderTests
    {
        private static DomainAuditEvent Evento(string action = "U", string entityRef = null, string user = "dr.gomez",
            string category = "rest", string outcome = "0")
            => new DomainAuditEvent
            {
                Id = Guid.NewGuid(),
                Recorded = new DateTime(2026, 10, 7, 15, 30, 0),
                Action = action,
                Outcome = outcome,
                AgentUserId = user,
                EntityRef = entityRef ?? "Questionnaire/" + Guid.NewGuid(),
                Category = category
            };

        private static void AssertCumpleCardinalidades(FhirAuditEvent recurso)
        {
            var errores = new List<ValidationResult>();
            DotNetAttributeValidation.TryValidate(recurso, errores, recurse: true);
            Assert.True(errores.Count == 0, string.Join("; ", errores.Select(e => e.ErrorMessage)));
        }

        // Sin esto los tests de abajo pasarían aunque la validación no revisara nada.
        [Fact]
        public void LaValidacionDetectaUnRecursoSinLosObligatorios()
        {
            var errores = new List<ValidationResult>();
            DotNetAttributeValidation.TryValidate(new FhirAuditEvent(), errores, recurse: true);
            Assert.NotEmpty(errores);
        }

        [Theory]
        [InlineData("C", "rest", "Questionnaire/9dfc5f22-7f4b-4adf-a244-46d80d3b57d7")]
        [InlineData("U", "rest", "QuestionnaireResponse/9dfc5f22-7f4b-4adf-a244-46d80d3b57d7$finalize")]
        [InlineData("E", "export", "Report/export?questionnaire=9dfc5f22-7f4b-4adf-a244-46d80d3b57d7&rows=10")]
        [InlineData("R", "report", "Patient/HC-123$trend")]
        [InlineData(null, null, null)]
        public void TraeTodosLosObligatoriosDeR5(string action, string category, string entityRef)
        {
            var e = Evento(action, entityRef, category: category);
            if (entityRef == null) e.EntityRef = null;

            var recurso = FhirAuditEventBuilder.Build(e);

            Assert.NotNull(recurso.Code);
            Assert.NotNull(recurso.Recorded);
            Assert.NotNull(Assert.Single(recurso.Agent).Who);
            Assert.NotNull(recurso.Source?.Observer);
            AssertCumpleCardinalidades(recurso);
        }

        [Theory]
        [InlineData("C", "create")]
        [InlineData("R", "read")]
        [InlineData("U", "update")]
        [InlineData("D", "delete")]
        public void Code_SaleDeLaAccion(string action, string esperado)
        {
            var recurso = FhirAuditEventBuilder.Build(Evento(action));

            var coding = Assert.Single(recurso.Code.Coding);
            Assert.Equal("http://hl7.org/fhir/restful-interaction", coding.System);
            Assert.Equal(esperado, coding.Code);
        }

        [Fact]
        public void Code_DeUnaOperacion_LlevaSuNombre()
        {
            var recurso = FhirAuditEventBuilder.Build(Evento("U", "Questionnaire/" + Guid.NewGuid() + "$publish"));

            Assert.Equal("operation", Assert.Single(recurso.Code.Coding).Code);
            Assert.Equal("$publish", recurso.Code.Text);
        }

        [Fact]
        public void Recorded_SeMarcaComoUtc()
        {
            var recurso = FhirAuditEventBuilder.Build(Evento());

            Assert.Equal(TimeSpan.Zero, recurso.Recorded.Value.Offset);
            Assert.Equal(new DateTime(2026, 10, 7, 15, 30, 0), recurso.Recorded.Value.DateTime);
        }

        [Fact]
        public void Agent_IdentificaAlUsuario()
        {
            var who = FhirAuditEventBuilder.Build(Evento(user: "dr.gomez")).Agent.Single().Who;

            Assert.Equal("dr.gomez", who.Identifier.Value);
            Assert.Equal(FhirAuditEventBuilder.UserSystem, who.Identifier.System);
        }

        // agent.who es obligatorio: sin usuario se declara ausente, no se inventa uno.
        [Theory]
        [InlineData(null)]
        [InlineData(" ")]
        public void Agent_SinUsuario_DeclaraElDatoAusente(string user)
        {
            var who = FhirAuditEventBuilder.Build(Evento(user: user)).Agent.Single().Who;

            Assert.Null(who.Identifier);
            Assert.Null(who.Display);
            var motivo = who.GetExtensionValue<Code>("http://hl7.org/fhir/StructureDefinition/data-absent-reason");
            Assert.Equal("unknown", motivo.Value);
        }

        // La referencia no puede arrastrar el sufijo de operación: "Questionnaire/{id}$publish" no es leíble.
        [Fact]
        public void Entity_DeUnRecursoFhir_EsUnaReferenciaLimpia()
        {
            var id = Guid.NewGuid();

            var what = FhirAuditEventBuilder.Build(Evento("U", "Questionnaire/" + id + "$publish")).Entity.Single().What;

            Assert.Equal("Questionnaire/" + id, what.Reference);
        }

        // Un reporte no es un recurso FHIR: va como identificador, no como referencia rota.
        [Fact]
        public void Entity_QueNoEsRecursoFhir_VaComoIdentificador()
        {
            const string entityRef = "Report/export?questionnaire=9dfc5f22-7f4b-4adf-a244-46d80d3b57d7&rows=10";

            var what = FhirAuditEventBuilder.Build(Evento("E", entityRef, category: "export")).Entity.Single().What;

            Assert.Null(what.Reference);
            Assert.Equal(entityRef, what.Identifier.Value);
            Assert.Equal(FhirAuditEventBuilder.EntitySystem, what.Identifier.System);
        }

        [Theory]
        [InlineData("0", "success")]
        [InlineData("4", "warning")]
        [InlineData("8", "error")]
        [InlineData("12", "fatal")]
        public void Outcome_TraduceLosCodigosDeR4(string guardado, string esperado)
        {
            var recurso = FhirAuditEventBuilder.Build(Evento(outcome: guardado));

            Assert.Equal(esperado, recurso.Outcome.Code.Code);
        }

        [Fact]
        public void Outcome_Desconocido_SeOmite()
        {
            Assert.Null(FhirAuditEventBuilder.Build(Evento(outcome: "x")).Outcome);
        }
    }
}
