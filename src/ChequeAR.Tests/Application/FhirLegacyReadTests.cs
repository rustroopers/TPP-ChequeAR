using ChequeAR.Application.Fhir;
using Xunit;

namespace ChequeAR.Tests.Application
{
    /// <summary>
    /// Red de contención sobre lo YA PUBLICADO.
    ///
    /// Las versiones congeladas antes de la feature 009 llevan vocabulario R4 ('choice',
    /// 'open-choice') que R5 eliminó, y son inmutables por diseño: hay fichas clínicas que las
    /// referencian, y el médico firmó lo que leyó. Medido sobre la base de desarrollo, 9 de
    /// las 20 versiones publicadas están en esa situación.
    ///
    /// Si el reader deja de leerlas, esos cuestionarios desaparecen para los clínicos. Es una
    /// regresión de FR-014 y FR-023, y sería silenciosa: no rompe ninguna compilación.
    /// Ver research.md (R8).
    /// </summary>
    public class FhirLegacyReadTests
    {
        // Recorte real de una versión congelada por el builder artesanal.
        private const string LegadoR4 = @"{
          ""resourceType"": ""Questionnaire"",
          ""id"": ""8f1e0c2a-0000-0000-0000-000000000001"",
          ""title"": ""Control de seguimiento"",
          ""version"": ""2"",
          ""status"": ""active"",
          ""item"": [
            { ""linkId"": ""dolor"", ""text"": ""¿Presenta dolor?"", ""type"": ""choice"", ""required"": true,
              ""answerOption"": [
                { ""valueCoding"": { ""code"": ""si"", ""display"": ""Sí"" } },
                { ""valueCoding"": { ""code"": ""no"", ""display"": ""No"" } }
              ] },
            { ""linkId"": ""otros"", ""text"": ""Otros síntomas"", ""type"": ""open-choice"",
              ""answerOption"": [ { ""valueCoding"": { ""code"": ""fiebre"", ""display"": ""Fiebre"" } } ] }
          ]
        }";

        [Fact]
        public void VersionCongeladaConVocabularioR4_SeSigueLeyendo()
        {
            var leido = FhirQuestionnaireReader.Read(LegadoR4);

            Assert.NotNull(leido);
            Assert.Equal("Control de seguimiento", leido.Title);
            Assert.Equal("2", leido.Version);
        }

        /// <summary>
        /// El tipo tiene que volver en el vocabulario de las islas Vue, no en el de FHIR:
        /// si vuelve "choice" crudo funciona por casualidad, pero si volviera "coding" el
        /// renderizador no sabría dibujar la pregunta.
        /// </summary>
        [Fact]
        public void LegadoChoice_VuelveComoChoice()
        {
            var pregunta = PrimeraPregunta(LegadoR4, 0);

            Assert.Equal("choice", pregunta.Type);
            Assert.True(pregunta.Required);
            Assert.Equal(new[] { "Sí", "No" }, pregunta.Options);
        }

        [Fact]
        public void LegadoOpenChoice_VuelveComoOpenChoice()
        {
            Assert.Equal("openChoice", PrimeraPregunta(LegadoR4, 1).Type);
        }

        // ---- Y el vocabulario nuevo también, obviamente ----

        [Fact]
        public void R5Coding_SinConstraint_EsOpcionCerrada()
        {
            var json = UnItem(@"""type"": ""coding""");
            Assert.Equal("choice", PrimeraPregunta(json, 0).Type);
        }

        [Fact]
        public void R5Coding_ConOptionsOrString_EsOpcionAbierta()
        {
            var json = UnItem(@"""type"": ""coding"", ""answerConstraint"": ""optionsOrString""");
            Assert.Equal("openChoice", PrimeraPregunta(json, 0).Type);
        }

        [Fact]
        public void R5Coding_ConOptionsOnly_EsOpcionCerrada()
        {
            var json = UnItem(@"""type"": ""coding"", ""answerConstraint"": ""optionsOnly""");
            Assert.Equal("choice", PrimeraPregunta(json, 0).Type);
        }

        [Fact]
        public void RecursoIlegible_DevuelveNullEnVezDeExplotar()
        {
            // La pantalla se degrada con un aviso; no se cae.
            Assert.Null(FhirQuestionnaireReader.Read("{ esto no es json"));
            Assert.Null(FhirQuestionnaireReader.Read(""));
            Assert.Null(FhirQuestionnaireReader.Read(null));
        }

        private static string UnItem(string atributos) => @"{
          ""resourceType"": ""Questionnaire"", ""status"": ""active"", ""title"": ""t"",
          ""item"": [ { ""linkId"": ""x"", ""text"": ""x"", " + atributos + @" } ] }";

        private static RenderableQuestion PrimeraPregunta(string json, int indice)
        {
            var leido = FhirQuestionnaireReader.Read(json);
            Assert.NotNull(leido);
            // Las preguntas sueltas se juntan en una sección implícita.
            return Assert.Single(leido.Groups).Questions[indice];
        }
    }
}
