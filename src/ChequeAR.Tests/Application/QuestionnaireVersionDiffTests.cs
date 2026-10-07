using System;
using System.Collections.Generic;
using System.Linq;
using ChequeAR.Application.Fhir;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using Xunit;

namespace ChequeAR.Tests.Application
{
    public class QuestionnaireVersionDiffTests
    {
        private static Questionnaire Q(string title, params Question[] preguntas)
        {
            var q = new Questionnaire { Title = title };
            var g = new QuestionGroup { Id = Guid.NewGuid(), Title = "Antropometría", OrderIndex = 0 };
            q.Groups.Add(g);
            var i = 0;
            foreach (var p in preguntas)
            {
                p.GroupId = g.Id;
                p.OrderIndex = i++;
                q.Questions.Add(p);
            }
            return q;
        }

        private static Question P(string linkId, string text, QuestionType type = QuestionType.Decimal,
            bool required = false, string validation = null)
            => new Question { LinkId = linkId, Text = text, Type = type, Required = required, ValidationJson = validation };

        // La misma versión contra sí misma no puede reportar nada: si reportara, cada
        // comparación tendría ruido y nadie le creería al resto.
        [Fact]
        public void Compare_VersionesIguales_SinCambios()
        {
            var a = Q("Control", P("peso-1", "Peso"), P("talla-1", "Talla"));
            var b = Q("Control", P("peso-1", "Peso"), P("talla-1", "Talla"));

            var d = QuestionnaireVersionDiff.Compare(a, b);

            Assert.False(d.HasChanges);
            Assert.Equal(2, d.Count(QuestionChangeKind.Unchanged));
        }

        // La identidad es el LinkId: cambiar el enunciado es una edición de la misma pregunta,
        // no una baja más un alta. Las respuestas siguen cayendo en la misma columna.
        [Fact]
        public void Compare_CambiaEnunciado_EsModificacion()
        {
            var a = Q("Control", P("peso-1", "Peso"));
            var b = Q("Control", P("peso-1", "Peso al nacer"));

            var d = QuestionnaireVersionDiff.Compare(a, b);

            var q = Assert.Single(d.Questions);
            Assert.Equal(QuestionChangeKind.Modified, q.Kind);
            var c = Assert.Single(q.Changes);
            Assert.Equal("text", c.Field);
            Assert.Equal("Peso", c.Before);
            Assert.Equal("Peso al nacer", c.After);
        }

        [Fact]
        public void Compare_AltasYBajas_SeDetectanYLasQuitadasVanAlFinal()
        {
            var a = Q("Control", P("peso-1", "Peso"), P("talla-1", "Talla"));
            var b = Q("Control", P("fc-1", "Frecuencia cardíaca"), P("peso-1", "Peso"));

            var d = QuestionnaireVersionDiff.Compare(a, b);

            Assert.Equal(new[] { "fc-1", "peso-1", "talla-1" }, d.Questions.Select(x => x.LinkId));
            Assert.Equal(QuestionChangeKind.Added, d.Questions[0].Kind);
            Assert.Equal(QuestionChangeKind.Unchanged, d.Questions[1].Kind);
            Assert.Equal(QuestionChangeKind.Removed, d.Questions[2].Kind);
        }

        [Fact]
        public void Compare_ObligatoriaRangoYUnidad_SeReportanPorCampo()
        {
            var a = Q("Control", P("peso-1", "Peso", validation: "{\"unit\":\"kg\",\"min\":0,\"max\":200}"));
            var b = Q("Control", P("peso-1", "Peso", required: true, validation: "{\"unit\":\"g\",\"min\":0,\"max\":300}"));

            var d = QuestionnaireVersionDiff.Compare(a, b);

            var campos = d.Questions.Single().Changes.Select(c => c.Field).ToList();
            Assert.Equal(new[] { "required", "unit", "range" }, campos);
        }

        [Fact]
        public void Compare_TituloCambiado_SeMarca()
        {
            var d = QuestionnaireVersionDiff.Compare(Q("Control"), Q("Control anual"));

            Assert.True(d.TitleChanged);
            Assert.True(d.HasChanges);
        }

        [Fact]
        public void Compare_OpcionesDistintas_SeReportan()
        {
            var a = P("sev-1", "Severidad", QuestionType.Choice);
            a.Options.Add(new AnswerOption { Display = "Leve", OrderIndex = 0 });
            var b = P("sev-1", "Severidad", QuestionType.Choice);
            b.Options.Add(new AnswerOption { Display = "Leve", OrderIndex = 0 });
            b.Options.Add(new AnswerOption { Display = "Severa", OrderIndex = 1 });

            var d = QuestionnaireVersionDiff.Compare(Q("C", a), Q("C", b));

            var c = Assert.Single(d.Questions.Single().Changes);
            Assert.Equal("options", c.Field);
            Assert.Equal("Leve · Severa", c.After);
        }

        [Fact]
        public void Compare_Nulos_NoLanza()
        {
            var d = QuestionnaireVersionDiff.Compare(null, Q("C", P("peso-1", "Peso")));

            Assert.Equal(QuestionChangeKind.Added, d.Questions.Single().Kind);
        }

        // La vista previa de una versión y la comparación leen el FHIR congelado. Ida y vuelta
        // por el builder: lo que se publicó tiene que volver con la misma forma que la entidad
        // viva (preguntas planas con GroupId), o el armado de pantalla no las encuentra.
        [Fact]
        public void ReadDomain_DesdeJsonPublicado_AplanaPreguntasConGrupo()
        {
            var original = Q("Control", P("peso-1", "Peso"), P("talla-1", "Talla"));
            var json = FhirQuestionnaireBuilder.Build(original, 1);

            var leido = FhirQuestionnaireReader.ReadDomain(json);

            Assert.Equal("Control", leido.Title);
            var g = Assert.Single(leido.Groups);
            Assert.Equal(2, leido.Questions.Count);
            Assert.All(leido.Questions, p => Assert.Equal(g.Id, p.GroupId));
            Assert.Empty(g.Questions);
            Assert.False(QuestionnaireVersionDiff.Compare(original, leido).HasChanges);
        }

        [Fact]
        public void ReadDomain_JsonInvalido_DevuelveNull()
        {
            Assert.Null(FhirQuestionnaireReader.ReadDomain("no es json"));
            Assert.Null(FhirQuestionnaireReader.ReadDomain(null));
        }

        // El código de una opción es lo que guardan las respuestas y sobre lo que agrupan los
        // reportes: cambiarlo con el mismo texto es un cambio, aunque en pantalla se vea igual.
        [Fact]
        public void Compare_CambiaCodigoDeOpcionConMismoTexto_EsModificacion()
        {
            var a = P("tabaco-1", "¿Fuma?", QuestionType.Choice);
            a.Options.Add(new AnswerOption { Code = "77176002", SnomedCode = "77176002", Display = "Fumador" });
            var b = P("tabaco-1", "¿Fuma?", QuestionType.Choice);
            b.Options.Add(new AnswerOption { Code = "65568007", SnomedCode = "65568007", Display = "Fumador" });

            var d = QuestionnaireVersionDiff.Compare(Q("Control", a), Q("Control", b));

            var c = Assert.Single(Assert.Single(d.Questions).Changes);
            Assert.Equal("options", c.Field);
            Assert.Contains("77176002", c.Before);
            Assert.Contains("65568007", c.After);
        }

        [Fact]
        public void Compare_CambiosDeValidacionYRepeticion_SeReportan()
        {
            var a = P("dni-1", "DNI", QuestionType.String, validation: "{\"maxLength\":8}");
            var b = P("dni-1", "DNI", QuestionType.String, validation: "{\"maxLength\":10,\"pattern\":\"^[0-9]+$\"}");
            b.RepeatsAllowed = true;

            var d = QuestionnaireVersionDiff.Compare(Q("Control", a), Q("Control", b));

            var campos = Assert.Single(d.Questions).Changes.Select(x => x.Field).ToList();
            Assert.Contains("maxLength", campos);
            Assert.Contains("pattern", campos);
            Assert.Contains("repeats", campos);
        }

        [Fact]
        public void Compare_CambiaDescripcion_HayCambios()
        {
            var a = Q("Control", P("peso-1", "Peso"));
            var b = Q("Control", P("peso-1", "Peso"));
            b.Description = "Control anual";

            var d = QuestionnaireVersionDiff.Compare(a, b);

            Assert.True(d.DescriptionChanged);
            Assert.True(d.HasChanges);
        }
    }
}
