using System.Collections.Generic;
using System.Linq;
using ChequeAR.Application.Fhir;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Xunit;

namespace ChequeAR.Tests.Application
{
    /// <summary>
    /// El catálogo de errores de la API es un contrato: un integrador externo distingue
    /// "no existe" de "no tenés permiso" de "regla de negocio violada" por issue.code, no
    /// leyendo el texto. Si un código cambia, cambia el contrato.
    ///
    /// Referencia: specs/009-conformidad-fhir-capas/contracts/fhir-api.md §Catálogo.
    /// </summary>
    public class OperationOutcomeTests
    {
        [Fact]
        public void NotFound_UsaCodigoNotFound()
        {
            var issue = Único(OperationOutcomes.NotFound());
            Assert.Equal(OperationOutcome.IssueType.NotFound, issue.Code);
            Assert.Equal(OperationOutcome.IssueSeverity.Error, issue.Severity);
        }

        [Fact]
        public void Forbidden_UsaCodigoForbidden()
        {
            Assert.Equal(OperationOutcome.IssueType.Forbidden, Único(OperationOutcomes.Forbidden()).Code);
        }

        [Fact]
        public void Invalid_UsaCodigoInvalid()
        {
            Assert.Equal(OperationOutcome.IssueType.Invalid, Único(OperationOutcomes.Invalid()).Code);
        }

        [Fact]
        public void Unprocessable_UsaCodigoBusinessRule()
        {
            var issue = Único(OperationOutcomes.Unprocessable("No se puede publicar sin preguntas."));
            Assert.Equal(OperationOutcome.IssueType.BusinessRule, issue.Code);
            Assert.Equal("No se puede publicar sin preguntas.", issue.Diagnostics);
        }

        [Fact]
        public void PreconditionRequired_UsaCodigoRequired_YMencionaIfMatch()
        {
            var issue = Único(OperationOutcomes.PreconditionRequired());
            Assert.Equal(OperationOutcome.IssueType.Required, issue.Code);
            // El mensaje tiene que decir QUÉ falta: es lo que ve quien integra.
            Assert.Contains("If-Match", issue.Diagnostics);
        }

        // Las dos versiones tienen que viajar en el texto: sin eso, quien recibe el conflicto
        // no sabe si perdió un cambio o cien.
        [Fact]
        public void Conflict_InformaVersionEsperadaYActual()
        {
            var issue = Único(OperationOutcomes.Conflict(expectedVersion: 3, actualVersion: 5));
            Assert.Equal(OperationOutcome.IssueType.Conflict, issue.Code);
            Assert.Contains("3", issue.Diagnostics);
            Assert.Contains("5", issue.Diagnostics);
        }

        [Fact]
        public void Transient_UsaCodigoTransient()
        {
            Assert.Equal(OperationOutcome.IssueType.Transient,
                         Único(OperationOutcomes.Transient("El servidor de terminología no responde.")).Code);
        }

        [Fact]
        public void Transient_ConEspera_LaIncluyeEnElTexto()
        {
            var issue = Único(OperationOutcomes.Transient("Demasiadas consultas.", retryAfterSeconds: 30));
            Assert.Contains("30", issue.Diagnostics);
        }

        [Fact]
        public void Informational_NoEsUnError()
        {
            var issue = Único(OperationOutcomes.Informational("Se archivó en vez de eliminarse."));
            Assert.Equal(OperationOutcome.IssueSeverity.Information, issue.Severity);
        }

        /// <summary>
        /// Un issue POR REGLA, no un mensaje concatenado. Es la diferencia entre "arreglá
        /// esto" y "arreglá algo de esta lista".
        /// </summary>
        [Fact]
        public void ForValidationErrors_EmiteUnIssuePorRegla()
        {
            var outcome = OperationOutcomes.ForValidationErrors(
                new[] { "linkId duplicado: q1", "falta useContext de especialidad" });

            Assert.Equal(2, outcome.Issue.Count);
            Assert.All(outcome.Issue, i => Assert.Equal(OperationOutcome.IssueSeverity.Error, i.Severity));
            Assert.Contains(outcome.Issue, i => i.Diagnostics.Contains("linkId duplicado"));
            Assert.Contains(outcome.Issue, i => i.Diagnostics.Contains("useContext"));
        }

        [Fact]
        public void ForValidationErrors_SinMensajes_NoDevuelveUnOutcomeVacio()
        {
            // Un OperationOutcome sin issues es inválido en FHIR y además no le dice nada a
            // nadie. Preferimos un mensaje pobre antes que un recurso mal formado.
            var outcome = OperationOutcomes.ForValidationErrors(new string[0]);
            Assert.Single(outcome.Issue);
        }

        [Fact]
        public void Serializa_ComoRecursoFhirValido()
        {
            var json = new FhirJsonSerializer().SerializeToString(OperationOutcomes.NotFound());

            Assert.Contains("\"resourceType\":\"OperationOutcome\"", json);
            Assert.Contains("\"not-found\"", json);
        }

        /// <summary>
        /// TODAS las fábricas tienen que producir recursos que sobrevivan el parser ESTRICTO.
        /// Un error mal formado es peor que no tener detalle: rompe al cliente justo cuando
        /// algo ya salió mal, y el problema real queda tapado por el problema del error.
        /// </summary>
        [Fact]
        public void TodasLasFabricas_ProducenRecursosQueSobrevivenElParserEstricto()
        {
            var casos = new Dictionary<string, OperationOutcome>
            {
                ["NotFound"] = OperationOutcomes.NotFound(),
                ["Forbidden"] = OperationOutcomes.Forbidden(),
                ["Invalid"] = OperationOutcomes.Invalid(),
                ["Unprocessable"] = OperationOutcomes.Unprocessable("El cuestionario no tiene preguntas."),
                ["PreconditionRequired"] = OperationOutcomes.PreconditionRequired(),
                ["Conflict"] = OperationOutcomes.Conflict(3, 5),
                ["Transient"] = OperationOutcomes.Transient("Servicio no disponible.", 30),
                ["Informational"] = OperationOutcomes.Informational("Se archivó en vez de eliminarse."),
                ["Validación"] = OperationOutcomes.ForValidationErrors(new[] { "regla 1", "regla 2" })
            };

            var serializer = new FhirJsonSerializer();
            var parser = new FhirJsonParser();   // estricto, sin ParserSettings permisivo

            foreach (var caso in casos)
            {
                var json = serializer.SerializeToString(caso.Value);
                var vuelta = parser.Parse<OperationOutcome>(json);

                Assert.True(vuelta.Issue.Count > 0, caso.Key + " quedó sin issues");
                Assert.All(vuelta.Issue, i =>
                {
                    Assert.NotNull(i.Code);
                    Assert.False(string.IsNullOrWhiteSpace(i.Diagnostics), caso.Key + " sin diagnostics");
                });
            }
        }

        private static OperationOutcome.IssueComponent Único(OperationOutcome outcome)
        {
            Assert.NotNull(outcome);
            return outcome.Issue.Single();
        }
    }
}
