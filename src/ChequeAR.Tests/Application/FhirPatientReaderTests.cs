using System;
using ChequeAR.Application.Services;
using Xunit;

namespace ChequeAR.Tests.Application
{
    /// <summary>
    /// Lectura de datos demográficos del Patient embebido en el FhirJson de una respuesta.
    /// Es la fuente en modo degradado (hospital caído), así que tiene que tolerar JSON
    /// incompleto o ilegible sin romper.
    /// </summary>
    public class FhirPatientReaderTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void JsonVacio_DevuelveNull(string json)
        {
            Assert.Null(FhirPatientReader.Read(json));
        }

        [Fact]
        public void JsonIlegible_DevuelveNull()
        {
            Assert.Null(FhirPatientReader.Read("{ esto no es json"));
        }

        [Fact]
        public void SoloSubjectDisplay_LoUsaComoNombre()
        {
            var json = "{\"resourceType\":\"QuestionnaireResponse\",\"subject\":{\"display\":\"Juan Pérez\"}}";
            var demo = FhirPatientReader.Read(json);
            Assert.Equal("Juan Pérez", demo.Name);
            Assert.Null(demo.BirthDate);
            Assert.Null(demo.AgeLabel);
        }

        [Fact]
        public void ContainedPatient_TienePrioridadYAportaSexoYNacimiento()
        {
            var json =
                "{\"resourceType\":\"QuestionnaireResponse\"," +
                "\"subject\":{\"display\":\"fallback\"}," +
                "\"contained\":[{\"resourceType\":\"Patient\"," +
                "\"name\":[{\"text\":\"María González\"}]," +
                "\"gender\":\"female\",\"birthDate\":\"2015-06-01\"}]}";

            var demo = FhirPatientReader.Read(json);

            Assert.Equal("María González", demo.Name);   // pisa el subject.display
            Assert.Equal("female", demo.Gender);
            Assert.Equal(new DateTime(2015, 6, 1), demo.BirthDate);
        }

        [Fact]
        public void ReadName_DevuelveSoloElNombre()
        {
            var json = "{\"subject\":{\"display\":\"Ana\"}}";
            Assert.Equal("Ana", FhirPatientReader.ReadName(json));
        }

        [Fact]
        public void AgeLabel_NuloSinFechaDeNacimiento()
        {
            Assert.Null(new PatientDemographics().AgeLabel);
        }

        [Fact]
        public void AgeLabel_CumpleañosYaPasado_CuentaLosAñosCompletos()
        {
            var bd = DateTime.UtcNow.Date.AddYears(-10).AddDays(-1);   // cumplió hace poco
            Assert.Equal("10 años", new PatientDemographics { BirthDate = bd }.AgeLabel);
        }

        [Fact]
        public void AgeLabel_CumpleañosAunNoLlegado_RestaUnAño()
        {
            var bd = DateTime.UtcNow.Date.AddYears(-10).AddDays(2);    // todavía no cumple este año
            Assert.Equal("9 años", new PatientDemographics { BirthDate = bd }.AgeLabel);
        }
    }
}
