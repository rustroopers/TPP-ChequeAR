using System;
using System.Linq;
using ChequeAR.Application.Fhir;
using ChequeAR.Domain.Enums;
using Hl7.Fhir.Model;
using Xunit;

namespace ChequeAR.Tests.Application
{
    public class FhirSearchParamsTests
    {
        // Antes "in-progress" no parseaba contra el enum y el filtro se descartaba en silencio.
        [Theory]
        [InlineData("in-progress", ResponseStatus.InProgress)]
        [InlineData("completed", ResponseStatus.Completed)]
        [InlineData("amended", ResponseStatus.Amended)]
        [InlineData("entered-in-error", ResponseStatus.EnteredInError)]
        [InlineData("InProgress", ResponseStatus.InProgress)]
        public void Status_CodigosFhir(string valor, ResponseStatus esperado)
        {
            Assert.True(FhirSearchParams.TryParseStatus(valor, out var st));
            Assert.Equal(esperado, st);
        }

        [Theory]
        [InlineData("pendiente")]
        [InlineData("7")]
        public void Status_Desconocido_SeRechaza(string valor)
        {
            Assert.False(FhirSearchParams.TryParseStatus(valor, out _));
        }

        [Theory]
        [InlineData("2fbb2d50-f3bd-4b7c-8497-dcead0dd8878")]
        [InlineData("https://chequear.garrahan/Questionnaire/2fbb2d50-f3bd-4b7c-8497-dcead0dd8878")]
        [InlineData("https://chequear.garrahan/Questionnaire/2fbb2d50-f3bd-4b7c-8497-dcead0dd8878|3")]
        [InlineData("Questionnaire/2fbb2d50-f3bd-4b7c-8497-dcead0dd8878")]
        public void Questionnaire_IdCanonicalOReferencia(string valor)
        {
            Assert.True(FhirSearchParams.TryParseQuestionnaire(valor, out var id));
            Assert.Equal(Guid.Parse("2fbb2d50-f3bd-4b7c-8497-dcead0dd8878"), id);
        }

        [Fact]
        public void Questionnaire_Invalido_SeRechaza()
        {
            Assert.False(FhirSearchParams.TryParseQuestionnaire("Questionnaire/abc", out _));
        }

        [Fact]
        public void Subject_ConOSinTipo()
        {
            Assert.Equal("PAC-1", FhirSearchParams.ParseSubject("Patient/PAC-1"));
            Assert.Equal("PAC-1", FhirSearchParams.ParseSubject("PAC-1"));
            Assert.Null(FhirSearchParams.ParseSubject(" "));
        }

        [Fact]
        public void Bundle_LinksDePaginacionYModo()
        {
            var self = new Uri("https://h/fhir/r5/QuestionnaireResponse?subject=PAC-1&_count=10&_offset=10");
            var bundle = FhirBundles.SearchSet(new Resource[] { new QuestionnaireResponse { Id = "a" } }, 35, "https://h/fhir/r5", self, 10, 10);

            Assert.Equal(Bundle.SearchEntryMode.Match, bundle.Entry.Single().Search.Mode);
            Assert.Equal(self.ToString(), bundle.Link.Single(l => l.Relation == "self").Url);
            Assert.EndsWith("subject=PAC-1&_count=10&_offset=20", bundle.Link.Single(l => l.Relation == "next").Url);
            Assert.EndsWith("_offset=0", bundle.Link.Single(l => l.Relation == "previous").Url);
        }

        [Fact]
        public void Bundle_UltimaPagina_SinNext()
        {
            var self = new Uri("https://h/fhir/r5/QuestionnaireResponse?_offset=30&_count=10");
            var bundle = FhirBundles.SearchSet(Array.Empty<Resource>(), 35, "https://h/fhir/r5", self, 30, 10);

            Assert.DoesNotContain(bundle.Link, l => l.Relation == "next");
        }
    }
}
