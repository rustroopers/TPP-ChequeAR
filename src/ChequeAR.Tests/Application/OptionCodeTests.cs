using System;
using System.Collections.Generic;
using System.Linq;
using ChequeAR.Application.Fhir;
using ChequeAR.Application.Reports;
using ChequeAR.Application.Services;
using ChequeAR.Application.Validation;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ChequeAR.Tests.Application
{
    /// <summary>
    /// Las respuestas de opción guardan el código de la opción, no el texto que ve el médico.
    /// Caso de referencia: "Fiebre" con la opción "Sí" codificada en SNOMED.
    /// </summary>
    public class OptionCodeTests
    {
        private const string Si = "373066001";
        private const string No = "373067005";

        private static RenderableQuestionnaire Version()
        {
            var q = new Questionnaire { Id = Guid.NewGuid(), Title = "Test" };
            q.Questions.Add(new Question
            {
                LinkId = "fiebre", Text = "Fiebre", Type = QuestionType.Choice, OrderIndex = 0,
                Options =
                {
                    new AnswerOption { Value = "si", Display = "Sí", Code = Si, SnomedCode = Si, SnomedDisplay = "Yes", OrderIndex = 0 },
                    new AnswerOption { Value = "no", Display = "No", Code = No, SnomedCode = No, SnomedDisplay = "No", OrderIndex = 1 }
                }
            });
            q.Questions.Add(new Question
            {
                LinkId = "temp", Text = "Temperatura", Type = QuestionType.Decimal, OrderIndex = 1,
                EnableWhenJson = "{\"question\":\"fiebre\",\"op\":\"=\",\"value\":\"Sí\"}"
            });
            return FhirQuestionnaireReader.Read(FhirQuestionnaireBuilder.Build(q, 1));
        }

        private static RenderableQuestion Fiebre(RenderableQuestionnaire v) =>
            v.Groups.SelectMany(g => g.Questions).Single(x => x.LinkId == "fiebre");

        [Fact]
        public void Lector_DevuelveCodigoSystemYEtiqueta()
        {
            var opcion = Fiebre(Version()).Options[0];

            Assert.Equal(Si, opcion.Code);
            Assert.Equal(TerminologyService.SnomedSystem, opcion.System);
            Assert.Equal("Sí", opcion.Display);
            Assert.Contains("Yes", opcion.Aliases);
        }

        [Fact]
        public void Lector_CondicionSobreOpcion_EsperaElCodigo()
        {
            var temp = Version().Groups.SelectMany(g => g.Questions).Single(x => x.LinkId == "temp");

            Assert.Equal(Si, temp.EnableWhen.Value);
        }

        [Fact]
        public void Lector_OpcionValueString_NoSeDescarta()
        {
            var json = new JObject
            {
                ["resourceType"] = "Questionnaire",
                ["status"] = "active",
                ["item"] = new JArray(new JObject
                {
                    ["linkId"] = "x", ["text"] = "X", ["type"] = "coding",
                    ["answerOption"] = new JArray(new JObject { ["valueString"] = "Otro" })
                })
            }.ToString();

            var opcion = Assert.Single(FhirQuestionnaireReader.Read(json).Groups[0].Questions[0].Options);
            Assert.Equal("Otro", opcion.Code);
            Assert.Equal("Otro", opcion.Display);
        }

        [Theory]
        [InlineData("Sí")]      // etiqueta, lo que guardaba el renderer
        [InlineData("Yes")]     // término SNOMED, display de versiones anteriores
        [InlineData(Si)]        // ya es el código
        public void Normalize_LlevaAlCodigo(string guardado)
        {
            var item = new QuestionnaireResponseItem { LinkId = "fiebre", ValueCode = guardado };

            OptionCodes.Normalize(Version(), new[] { item });

            Assert.Equal(Si, item.ValueCode);
        }

        [Fact]
        public void Normalize_NoTocaLoQueNoReconoce()
        {
            var desconocido = new QuestionnaireResponseItem { LinkId = "fiebre", ValueCode = "Quizás" };
            var otraPregunta = new QuestionnaireResponseItem { LinkId = "temp", ValueString = "Sí" };

            var cambios = OptionCodes.Normalize(Version(), new[] { desconocido, otraPregunta });

            Assert.Equal(0, cambios);
            Assert.Equal("Quizás", desconocido.ValueCode);
        }

        [Fact]
        public void FindOption_AliasAmbiguo_NoResuelve()
        {
            var pregunta = new RenderableQuestion
            {
                Options =
                {
                    new RenderableOption { Code = "a", Display = "Leve", Aliases = { "Leve" } },
                    new RenderableOption { Code = "b", Display = "Leve", Aliases = { "Leve" } }
                }
            };

            Assert.Null(pregunta.FindOption("Leve"));
            Assert.Equal("b", pregunta.FindOption("b").Code);
        }

        [Fact]
        public void FindOption_AliasHaciaUnCodigoRepetido_NoResuelve()
        {
            var pregunta = new RenderableQuestion
            {
                Options =
                {
                    new RenderableOption { Code = "x", Display = "Leve", Aliases = { "Leve" } },
                    new RenderableOption { Code = "x", Display = "Moderada", Aliases = { "Moderada" } }
                }
            };

            Assert.Null(pregunta.FindOption("Moderada"));
        }

        private static RenderableQuestionnaire ConOpcion(string code, string display) => new RenderableQuestionnaire
        {
            Groups =
            {
                new RenderableGroup
                {
                    Questions =
                    {
                        new RenderableQuestion
                        {
                            LinkId = "sev",
                            Options = { new RenderableOption { Code = code, Display = display, Aliases = { display } } }
                        }
                    }
                }
            }
        };

        [Fact]
        public void Carry_CambioDeCodigoConLaMismaEtiqueta_ConservaLaRespuesta()
        {
            var item = new QuestionnaireResponseItem { LinkId = "sev", ValueCode = "255604002" };

            OptionCodes.Carry(ConOpcion("255604002", "Leve"), ConOpcion("leve", "Leve"), new[] { item });

            Assert.Equal("leve", item.ValueCode);
        }

        [Fact]
        public void Carry_CambioDeEtiquetaConElMismoCodigo_ConservaLaRespuesta()
        {
            var item = new QuestionnaireResponseItem { LinkId = "sev", ValueCode = "Mild" };

            OptionCodes.Carry(ConOpcion("255604002", "Mild"), ConOpcion("255604002", "Leve"), new[] { item });

            Assert.Equal("255604002", item.ValueCode);
        }

        [Fact]
        public void ClearOrphans_VaciaLasOpcionesQueYaNoExisten()
        {
            var huerfana = new QuestionnaireResponseItem { LinkId = "sev", ValueCode = "grave" };
            var valida = new QuestionnaireResponseItem { LinkId = "sev", ValueCode = "leve" };

            var afectadas = OptionCodes.ClearOrphans(ConOpcion("leve", "Leve"), new[] { huerfana, valida });

            Assert.Equal(new[] { "sev" }, afectadas);
            Assert.Null(huerfana.ValueCode);
            Assert.Equal("leve", valida.ValueCode);
        }

        [Theory]
        [InlineData(Si)]
        [InlineData("Sí")]
        public void Respuesta_EmiteElMismoCodingQueElAnswerOption(string guardado)
        {
            var r = new QuestionnaireResponse
            {
                Id = Guid.NewGuid(), QuestionnaireId = Guid.NewGuid(), PatientHospitalId = "1",
                Items = new List<QuestionnaireResponseItem> { new QuestionnaireResponseItem { LinkId = "fiebre", ValueCode = guardado } }
            };

            var coding = JObject.Parse(FhirQuestionnaireResponseBuilder.Build(r, Version()))["item"][0]["answer"][0]["valueCoding"];

            Assert.Equal(TerminologyService.SnomedSystem, (string)coding["system"]);
            Assert.Equal(Si, (string)coding["code"]);
            Assert.Equal("Sí", (string)coding["display"]);
        }

        [Fact]
        public void Formatter_MuestraLaEtiqueta()
        {
            var item = new QuestionnaireResponseItem { LinkId = "fiebre", ValueCode = Si };

            Assert.Equal("Sí", ResponseAnswerFormatter.Format(new[] { item }, Fiebre(Version())));
            Assert.Equal(Si, ResponseAnswerFormatter.Format(item));
        }

        [Fact]
        public void Reporte_EtiquetaLaOpcionYTambienElTerminoViejo()
        {
            var q = new Questionnaire { Id = Guid.NewGuid() };
            q.Questions.Add(new Question
            {
                LinkId = "fiebre", Text = "Fiebre", Type = QuestionType.Choice,
                Options = { new AnswerOption { Value = "si", Display = "Sí", Code = Si, SnomedCode = Si, SnomedDisplay = "Yes" } }
            });
            var schema = ReportSchema.From(q);

            Assert.Equal("Sí", schema.LabelFor("fiebre", Si));
            Assert.Equal("Sí", schema.LabelFor("fiebre", "Yes"));

            var largo = new ReportResult
            {
                Rows = new List<Dictionary<string, object>>
                {
                    new Dictionary<string, object> { ["ResponseId"] = "r1", ["LinkId"] = "fiebre", ["ValueText"] = Si }
                }
            };
            var ancho = ReportPivot.ToWide(largo, schema.Columns, schema.LabelFor);

            Assert.Equal("Sí", ancho.Rows[0]["Fiebre"]);
        }
    }
}
