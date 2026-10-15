using System;
using System.Collections.Generic;
using System.Linq;
using ChequeAR.Application.Fhir;
using ChequeAR.Application.Validation;
using ChequeAR.Domain.Entities;
using Xunit;

namespace ChequeAR.Tests.Application
{
    public class ResponseValidatorTests
    {
        private static RenderableQuestionnaire Estructura()
        {
            var s = new RenderableQuestionnaire();
            s.Groups.Add(new RenderableGroup
            {
                Questions =
                {
                    new RenderableQuestion { LinkId = "edad", Text = "Edad", Type = "integer", Min = 0, Max = 18 },
                    new RenderableQuestion { LinkId = "temp", Text = "Temperatura", Type = "decimal", Min = 30, Max = 45 },
                    new RenderableQuestion { LinkId = "dolor", Text = "Dolor", Type = "scale", Min = 0, Max = 10 },
                    new RenderableQuestion { LinkId = "dni", Text = "DNI", Type = "string", MaxLength = 8, Pattern = "^[0-9]+$" },
                    new RenderableQuestion { LinkId = "obs", Text = "Observaciones", Type = "text", Pattern = "([" },
                    new RenderableQuestion { LinkId = "sev", Text = "Severidad", Type = "choice", Options = { new RenderableOption { Code = "leve" } } },
                    new RenderableQuestion { LinkId = "sint", Text = "Síntomas", Type = "openChoice", Options = { new RenderableOption { Code = "tos" }, new RenderableOption { Code = "fiebre" } } },
                    new RenderableQuestion { LinkId = "rx", Text = "Rx", Type = "attachment" },
                    new RenderableQuestion { LinkId = "fiebre", Text = "Fiebre", Type = "boolean" }
                }
            });
            return s;
        }

        private static List<string> Validar(bool complete, params QuestionnaireResponseItem[] items)
            => ResponseValidator.Validate(Estructura(), items, complete);

        [Fact]
        public void RespuestasValidas_NoHayErrores()
        {
            Assert.Empty(Validar(true,
                new QuestionnaireResponseItem { LinkId = "edad", ValueInteger = 7 },
                new QuestionnaireResponseItem { LinkId = "temp", ValueDecimal = 37.5m },
                new QuestionnaireResponseItem { LinkId = "dolor", ValueDecimal = 4 },
                new QuestionnaireResponseItem { LinkId = "dni", ValueString = "12345678" },
                new QuestionnaireResponseItem { LinkId = "obs", ValueString = "lo que sea" },
                new QuestionnaireResponseItem { LinkId = "sev", ValueCode = "leve" },
                new QuestionnaireResponseItem { LinkId = "sint", ValueCode = "tos" },
                new QuestionnaireResponseItem { LinkId = "sint", ValueCode = "fiebre" },
                new QuestionnaireResponseItem { LinkId = "rx", AttachmentId = Guid.NewGuid(), ValueString = "rx.pdf" },
                new QuestionnaireResponseItem { LinkId = "fiebre", ValueBoolean = false },
                new QuestionnaireResponseItem { LinkId = "edad" }));
        }

        [Theory]
        [InlineData("edad", 19)]
        [InlineData("edad", -1)]
        [InlineData("temp", 46)]
        [InlineData("dolor", 11)]
        public void FueraDeRango_AlFinalizar(string linkId, int valor)
        {
            var item = linkId == "edad"
                ? new QuestionnaireResponseItem { LinkId = linkId, ValueInteger = valor }
                : new QuestionnaireResponseItem { LinkId = linkId, ValueDecimal = valor };

            Assert.Single(Validar(true, item));
            Assert.Empty(Validar(false, item));
        }

        [Fact]
        public void Escala_NoEntera()
        {
            Assert.Single(Validar(false, new QuestionnaireResponseItem { LinkId = "dolor", ValueDecimal = 4.5m }));
        }

        [Fact]
        public void Escala_ComoEnteroDeUnClienteFhir()
        {
            Assert.Empty(Validar(true, new QuestionnaireResponseItem { LinkId = "dolor", ValueInteger = 4 }));
        }

        [Theory]
        [InlineData("123456789")]   // largo
        [InlineData("12a45")]       // patrón
        public void Texto_LargoOFormato(string valor)
        {
            Assert.Single(Validar(true, new QuestionnaireResponseItem { LinkId = "dni", ValueString = valor }));
        }

        [Theory]
        [InlineData("(?i)^[a-z]+$", "123")]   // no compila en JavaScript: el navegador lo ignora
        [InlineData("^\\w+$", "ñandú")]      // \w de JavaScript es ASCII: el navegador lo rechaza igual
        public void Patron_SeEvaluaComoEnElNavegador(string patron, string valor)
        {
            var s = new RenderableQuestionnaire();
            s.Groups.Add(new RenderableGroup { Questions = { new RenderableQuestion { LinkId = "x", Type = "string", Pattern = patron } } });
            var errores = ResponseValidator.Validate(s, new[] { new QuestionnaireResponseItem { LinkId = "x", ValueString = valor } }, true);

            if (patron.StartsWith("(?i)")) Assert.Empty(errores);
            else Assert.Single(errores);
        }

        [Fact]
        public void Patron_ParentesisEscapadoOpcional_SeAplica()
        {
            var s = new RenderableQuestionnaire();
            s.Groups.Add(new RenderableGroup { Questions = { new RenderableQuestion { LinkId = "tel", Type = "string", Pattern = @"^\(?\d{2}\)?$" } } });

            Assert.Empty(ResponseValidator.Validate(s, new[] { new QuestionnaireResponseItem { LinkId = "tel", ValueString = "(11)" } }, true));
            Assert.Single(ResponseValidator.Validate(s, new[] { new QuestionnaireResponseItem { LinkId = "tel", ValueString = "abc" } }, true));
        }

        [Fact]
        public void PatronInvalido_NoBloquea()
        {
            Assert.Empty(Validar(true, new QuestionnaireResponseItem { LinkId = "obs", ValueString = "x" }));
        }

        [Fact]
        public void OpcionInexistente_SeRechazaSiempre()
        {
            Assert.Single(Validar(false, new QuestionnaireResponseItem { LinkId = "sev", ValueCode = "grave" }));
        }

        [Fact]
        public void PreguntaInexistente_SeRechazaSiTieneValor()
        {
            Assert.Single(Validar(false, new QuestionnaireResponseItem { LinkId = "inventada", ValueString = "x" }));
            Assert.Empty(Validar(false, new QuestionnaireResponseItem { LinkId = "inventada" }));
        }

        [Fact]
        public void ColumnaQueNoCorrespondeAlTipo()
        {
            var errores = Validar(false,
                new QuestionnaireResponseItem { LinkId = "edad", ValueString = "siete" },
                new QuestionnaireResponseItem { LinkId = "fiebre", ValueCode = "si" },
                new QuestionnaireResponseItem { LinkId = "dni", AttachmentId = Guid.NewGuid() });

            Assert.Equal(3, errores.Count);
        }

        [Fact]
        public void VariasRespuestas_SoloEnLaMultiple()
        {
            var errores = Validar(false,
                new QuestionnaireResponseItem { LinkId = "sev", ValueCode = "leve" },
                new QuestionnaireResponseItem { LinkId = "sev", ValueCode = "leve" });

            Assert.Contains(errores, e => e.Contains("una sola respuesta"));
        }

        [Fact]
        public void SinEstructura_NoSeValida()
        {
            Assert.Empty(ResponseValidator.Validate(null, new[] { new QuestionnaireResponseItem { LinkId = "x", ValueString = "y" } }, true));
        }
    }
}
