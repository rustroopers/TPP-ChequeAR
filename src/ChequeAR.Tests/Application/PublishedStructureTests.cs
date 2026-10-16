using System;
using System.Linq;
using ChequeAR.Application.Fhir;
using ChequeAR.Application.Reports;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using Xunit;

namespace ChequeAR.Tests.Application
{
    public class PublishedStructureTests
    {
        private static QuestionnaireVersion Version(Questionnaire q, int numero, params Question[] preguntas)
        {
            var foto = new Questionnaire { Id = q.Id, Title = q.Title };
            foreach (var p in preguntas) foto.Questions.Add(p);
            var v = new QuestionnaireVersion { Id = Guid.NewGuid(), QuestionnaireId = q.Id, VersionNumber = numero, FhirJson = FhirQuestionnaireBuilder.Build(foto, numero) };
            q.Versions.Add(v);
            return v;
        }

        private static Question P(string linkId, string text, int orden = 0)
            => new Question { LinkId = linkId, Text = text, Type = QuestionType.Decimal, OrderIndex = orden };

        // Una pregunta que se sacó del borrador o de la vigente sigue en el reporte: tiene datos.
        [Fact]
        public void Union_VigenteEnSuOrdenYDespuesLasRetiradas()
        {
            var q = new Questionnaire { Id = Guid.NewGuid(), Title = "Control" };
            Version(q, 1, P("peso", "Peso"), P("talla", "Talla", 1));
            var v2 = Version(q, 2, P("talla", "Talla (cm)"), P("imc", "IMC", 1));
            q.CurrentVersionId = v2.Id;
            q.Questions.Add(P("nueva", "Sin publicar"));

            var union = PublishedStructure.Union(q);

            Assert.Equal(new[] { "talla", "imc", "peso" }, union.Questions.OrderBy(x => x.OrderIndex).Select(x => x.LinkId));
            Assert.Equal("Talla (cm)", union.Questions.Single(x => x.LinkId == "talla").Text);
        }

        // Una opción que la versión nueva de la variable quitó sigue teniendo etiqueta: las
        // fichas viejas guardaron su código.
        [Fact]
        public void Union_SumaLasOpcionesRetiradas()
        {
            var q = new Questionnaire { Id = Guid.NewGuid(), Title = "Control" };
            var v1 = new Question { LinkId = "sev", Text = "Severidad", Type = QuestionType.Choice };
            v1.Options.Add(new AnswerOption { Code = "leve", Display = "Leve" });
            v1.Options.Add(new AnswerOption { Code = "grave", Display = "Grave" });
            Version(q, 1, v1);
            var v2 = new Question { LinkId = "sev", Text = "Severidad", Type = QuestionType.Choice };
            v2.Options.Add(new AnswerOption { Code = "leve", Display = "Leve" });
            v2.Options.Add(new AnswerOption { Code = "moderada", Display = "Moderada" });
            q.CurrentVersionId = Version(q, 2, v2).Id;

            var schema = ReportSchema.From(PublishedStructure.Union(q));

            Assert.Equal("Grave", schema.LabelFor("sev", "grave"));
            Assert.Equal("Moderada", schema.LabelFor("sev", "moderada"));
        }

        [Fact]
        public void SinVersiones_NoHayPreguntas()
        {
            var q = new Questionnaire { Id = Guid.NewGuid() };
            q.Questions.Add(P("peso", "Peso"));

            Assert.Empty(PublishedStructure.Union(q).Questions);
        }

        [Fact]
        public void ReportSchema_ConLaUnion_ConoceLaPreguntaRetirada()
        {
            var q = new Questionnaire { Id = Guid.NewGuid(), Title = "Control" };
            Version(q, 1, P("peso", "Peso"));
            q.CurrentVersionId = Version(q, 2, P("talla", "Talla")).Id;

            var schema = ReportSchema.From(PublishedStructure.Union(q));

            Assert.Equal("ValueDecimal", schema.ColumnFor("peso"));
        }
    }
}
