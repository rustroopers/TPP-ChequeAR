using ChequeAR.Application.Fhir;
using Hl7.Fhir.Model;
using Xunit;

namespace ChequeAR.Tests.Application
{
    public class FhirUnsupportedItemsTests
    {
        private static Questionnaire.ItemComponent Item(string linkId, Questionnaire.QuestionnaireItemType type, params Questionnaire.ItemComponent[] hijos)
        {
            var item = new Questionnaire.ItemComponent { LinkId = linkId, Text = linkId, Type = type };
            item.Item.AddRange(hijos);
            return item;
        }

        [Fact]
        public void SeccionesYTiposConocidos_NoHayErrores()
        {
            var q = new Questionnaire();
            q.Item.Add(Item("g", Questionnaire.QuestionnaireItemType.Group,
                Item("peso", Questionnaire.QuestionnaireItemType.Decimal),
                Item("sev", Questionnaire.QuestionnaireItemType.Coding)));
            q.Item.Add(Item("obs", Questionnaire.QuestionnaireItemType.Text));

            Assert.Empty(FhirQuestionnaireParser.UnsupportedItems(q));
        }

        // Antes se convertía en una pregunta de texto y sus hijos se descartaban.
        [Fact]
        public void SubSeccion_SeRechaza()
        {
            var q = new Questionnaire();
            q.Item.Add(Item("g", Questionnaire.QuestionnaireItemType.Group,
                Item("sub", Questionnaire.QuestionnaireItemType.Group, Item("peso", Questionnaire.QuestionnaireItemType.Decimal))));

            Assert.Contains("subsecciones", Assert.Single(FhirQuestionnaireParser.UnsupportedItems(q)));
        }

        [Fact]
        public void TipoSinEquivalente_SeRechaza()
        {
            var q = new Questionnaire();
            q.Item.Add(Item("hora", Questionnaire.QuestionnaireItemType.Time));

            Assert.Contains("'time'", Assert.Single(FhirQuestionnaireParser.UnsupportedItems(q)));
        }
    }
}
