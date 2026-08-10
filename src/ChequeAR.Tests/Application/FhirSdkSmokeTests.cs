using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Xunit;

namespace ChequeAR.Tests.Application
{
    /// <summary>
    /// Compuerta de la feature 009 (tarea T006): verifica que el SDK de Firely carga y
    /// funciona sobre .NET Framework 4.7.1.
    ///
    /// No prueba lógica de ChequeAR: prueba el CABLEADO. Existe porque el riesgo dominante
    /// de la feature no era el código FHIR sino que el SDK ni siquiera cargara — la línea
    /// 6.x targetea netstandard2.1/net8.0 y no sirve en net471, así que estamos pinneados a
    /// 5.13.4, la última que publica un grupo netstandard2.0.
    ///
    /// Si esto falla con ReflectionTypeLoadException o FileLoadException, el problema no está
    /// en el test sino en los bindingRedirect: comparar la versión de assembly real de los
    /// DLL en bin/ contra lo declarado en Web.config. Ver research.md (R1).
    /// </summary>
    public class FhirSdkSmokeTests
    {
        [Fact]
        public void SdkFirely_SerializaYParsea_SobreNetFramework()
        {
            var questionnaire = new Questionnaire
            {
                Id = "smoke-009",
                Title = "Prueba de humo",
                Status = PublicationStatus.Active
            };
            questionnaire.Item.Add(new Questionnaire.ItemComponent
            {
                LinkId = "peso-nacer",
                Text = "Peso al nacer",
                Type = Questionnaire.QuestionnaireItemType.Decimal
            });

            var json = new FhirJsonSerializer().SerializeToString(questionnaire);

            // El serializador del SDK emite resourceType; el de Newtonsoft no lo haría.
            // Es justamente la diferencia que motiva el MediaTypeFormatter dedicado (T010).
            Assert.Contains("\"resourceType\":\"Questionnaire\"", json);

            var parsed = new FhirJsonParser().Parse<Questionnaire>(json);

            Assert.Equal("smoke-009", parsed.Id);
            Assert.Equal("peso-nacer", parsed.Item[0].LinkId);
            Assert.Equal(Questionnaire.QuestionnaireItemType.Decimal, parsed.Item[0].Type);
        }
    }
}
