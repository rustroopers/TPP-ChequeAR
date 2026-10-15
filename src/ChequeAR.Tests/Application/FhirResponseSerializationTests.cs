using System;
using System.Linq;
using ChequeAR.Application.Fhir;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ChequeAR.Tests.Application
{
    public class FhirResponseSerializationTests
    {
        /// <summary>
        /// Una opción múltiple guarda un registro por opción marcada, todos con el mismo
        /// linkId. En FHIR eso es UN item con varias entradas en answer, no varios items
        /// repetidos: un cliente que itere items esperaría preguntas distintas.
        /// </summary>
        [Fact]
        public void LinkIdRepetido_SeAgrupaEnUnItemConVariasRespuestas()
        {
            var r = Respuesta(
                Item("sintomas", code: "fiebre"),
                Item("sintomas", code: "tos"),
                Item("sintomas", code: "dolor"));

            var items = (JArray)Serializar(r)["item"];

            var item = Assert.Single(items);
            Assert.Equal("sintomas", (string)item["linkId"]);
            Assert.Equal(3, ((JArray)item["answer"]).Count);
        }

        /// <summary>
        /// Sin responder no es lo mismo que respondido con vacío: un answer vacío es inválido
        /// en FHIR y además afirma algo que el clínico no dijo.
        /// </summary>
        [Fact]
        public void PreguntaSinResponder_SeSerializaSinAnswer()
        {
            var r = Respuesta(new QuestionnaireResponseItem { LinkId = "vacia" });

            var item = Assert.Single((JArray)Serializar(r)["item"]);

            Assert.Equal("vacia", (string)item["linkId"]);
            Assert.Null(item["answer"]);
        }

        [Theory]
        [InlineData("valueBoolean")]
        [InlineData("valueInteger")]
        [InlineData("valueDecimal")]
        [InlineData("valueDate")]
        [InlineData("valueDateTime")]
        [InlineData("valueCoding")]
        [InlineData("valueString")]
        public void CadaTipoDeValor_UsaSuAnswerTipado(string esperado)
        {
            var item = esperado == "valueBoolean" ? new QuestionnaireResponseItem { LinkId = "x", ValueBoolean = true }
                     : esperado == "valueInteger" ? new QuestionnaireResponseItem { LinkId = "x", ValueInteger = 7 }
                     : esperado == "valueDecimal" ? new QuestionnaireResponseItem { LinkId = "x", ValueDecimal = 3.5m }
                     : esperado == "valueDate" ? new QuestionnaireResponseItem { LinkId = "x", ValueDate = new DateTime(2026, 3, 1) }
                     : esperado == "valueDateTime" ? new QuestionnaireResponseItem { LinkId = "x", ValueDate = new DateTime(2026, 3, 1, 10, 30, 0) }
                     : esperado == "valueCoding" ? new QuestionnaireResponseItem { LinkId = "x", ValueCode = "si" }
                     : new QuestionnaireResponseItem { LinkId = "x", ValueString = "texto" };

            var answer = ((JArray)Assert.Single((JArray)Serializar(Respuesta(item))["item"])["answer"])[0];

            Assert.NotNull(answer[esperado]);
        }

        /// <summary>
        /// El adjunto viaja por referencia porque el archivo ya está subido. La URL apunta al
        /// endpoint propio de adjuntos, que por eso tiene que seguir existiendo aunque no sea
        /// parte de la interfaz FHIR (FR-017).
        /// </summary>
        [Fact]
        public void Adjunto_ViajaComoReferenciaAlEndpointPropio()
        {
            var id = Guid.NewGuid();
            var r = Respuesta(new QuestionnaireResponseItem
            {
                LinkId = "radiografia",
                AttachmentId = id,
                ValueString = "rx-torax.pdf"
            });

            var answer = ((JArray)Assert.Single((JArray)Serializar(r)["item"])["answer"])[0];

            Assert.Equal("/api/attachments/" + id, (string)answer["valueAttachment"]["url"]);
            Assert.Equal("rx-torax.pdf", (string)answer["valueAttachment"]["title"]);
        }

        /// <summary>
        /// El Patient contenido NO es decorativo: cuando el hospital no responde es la única
        /// forma de saber de quién es la ficha. Si se pierde al regenerar el recurso, las
        /// pantallas de paciente quedan mostrando números de historia clínica.
        /// </summary>
        [Fact]
        public void DatosDelPaciente_SePreservanComoRecursoContenido()
        {
            var r = Respuesta(Item("x", code: "si"));

            var json = Serializar(r, new PatientDemographicsDto
            {
                FullName = "Pérez, Ana",
                Gender = "female",
                BirthDate = new DateTime(2018, 5, 12)
            });

            var contenido = ((JArray)json["contained"])[0];
            Assert.Equal("Patient", (string)contenido["resourceType"]);
            Assert.Equal("Pérez, Ana", (string)contenido["name"][0]["text"]);
            Assert.Equal("female", (string)contenido["gender"]);
            Assert.Equal("2018-05-12", (string)contenido["birthDate"]);
        }

        [Fact]
        public void SinDatosDelPaciente_NoSeEmiteRecursoContenido()
        {
            Assert.Null(Serializar(Respuesta(Item("x", code: "si")))["contained"]);
        }

        [Fact]
        public void PacienteSeIdentificaPorHistoriaClinica()
        {
            var json = Serializar(Respuesta(Item("x", code: "si")));

            Assert.Equal("https://chequear.garrahan/patient-hospital-id",
                         (string)json["subject"]["identifier"]["system"]);
            Assert.Equal("PAC-000001", (string)json["subject"]["identifier"]["value"]);
        }

        /// <summary>
        /// El anclaje de versión es lo que garantiza que la ficha refleje lo que el médico
        /// leyó. Sin él, una publicación posterior reinterpretaría fichas ya cerradas.
        /// </summary>
        [Fact]
        public void VersionAnclada_ViajaComoExtensionPropia()
        {
            var versionId = Guid.NewGuid();
            var r = Respuesta(Item("x", code: "si"));
            r.QuestionnaireVersionId = versionId;

            var extensiones = (JArray)Serializar(r)["extension"];

            Assert.Contains(extensiones,
                e => (string)e["url"] == "https://chequear.garrahan/StructureDefinition/questionnaire-version-id"
                  && (string)e["valueString"] == versionId.ToString());
        }

        [Theory]
        [InlineData(ResponseStatus.InProgress, "in-progress")]
        [InlineData(ResponseStatus.Completed, "completed")]
        [InlineData(ResponseStatus.Amended, "amended")]
        public void EstadoSeMapeaAlVocabularioDelEstandar(ResponseStatus estado, string esperado)
        {
            var r = Respuesta(Item("x", code: "si"));
            r.Status = estado;

            Assert.Equal(esperado, (string)Serializar(r)["status"]);
        }

        /// <summary>
        /// Los textos salen de la versión ANCLADA, no de la entidad viva: si alguien renombró
        /// la pregunta después, la ficha conserva el enunciado que se respondió.
        /// </summary>
        [Fact]
        public void ElTextoSaleDeLaVersionAnclada()
        {
            var anclada = new RenderableQuestionnaire
            {
                Groups =
                {
                    new RenderableGroup
                    {
                        Synthetic = true,
                        Questions = { new RenderableQuestion { LinkId = "peso", Text = "Peso al nacer (kg)" } }
                    }
                }
            };

            var json = JObject.Parse(FhirQuestionnaireResponseBuilder.Build(
                Respuesta(new QuestionnaireResponseItem { LinkId = "peso", ValueDecimal = 3.2m }), anclada));

            Assert.Equal("Peso al nacer (kg)", (string)((JArray)json["item"])[0]["text"]);
        }

        private static RenderableQuestionnaire ConSecciones() => new RenderableQuestionnaire
        {
            Groups =
            {
                new RenderableGroup
                {
                    LinkId = "vitales", Title = "Signos vitales",
                    Questions =
                    {
                        new RenderableQuestion { LinkId = "dolor", Type = "scale" },
                        new RenderableQuestion { LinkId = "inicio", Type = "date" },
                        new RenderableQuestion { LinkId = "control", Type = "dateTime" }
                    }
                },
                new RenderableGroup { LinkId = "vacia", Title = "Sin respuestas", Questions = { new RenderableQuestion { LinkId = "nada" } } },
                new RenderableGroup { Synthetic = true, LinkId = "sin-grupo", Questions = { new RenderableQuestion { LinkId = "obs", Type = "text" } } }
            }
        };

        // R5: los ítems de la ficha siguen la estructura del Questionnaire.
        [Fact]
        public void LasPreguntasVanDentroDeSuSeccion()
        {
            var json = JObject.Parse(FhirQuestionnaireResponseBuilder.Build(Respuesta(
                new QuestionnaireResponseItem { LinkId = "obs", ValueString = "x" },
                new QuestionnaireResponseItem { LinkId = "dolor", ValueDecimal = 4 },
                new QuestionnaireResponseItem { LinkId = "retirada", ValueString = "y" }), ConSecciones()));

            var items = (JArray)json["item"];
            Assert.Equal(new[] { "vitales", "obs", "retirada" }, items.Select(i => (string)i["linkId"]));
            Assert.Equal("Signos vitales", (string)items[0]["text"]);
            Assert.Equal("dolor", (string)items[0]["item"][0]["linkId"]);
        }

        [Fact]
        public void ElTipoDeLaPreguntaDecideElAnswer()
        {
            var json = JsonConvert.DeserializeObject<JObject>(FhirQuestionnaireResponseBuilder.Build(Respuesta(
                new QuestionnaireResponseItem { LinkId = "dolor", ValueDecimal = 4 },
                new QuestionnaireResponseItem { LinkId = "inicio", ValueDate = new DateTime(2024, 5, 1) },
                new QuestionnaireResponseItem { LinkId = "control", ValueDate = new DateTime(2024, 5, 1, 10, 30, 0) }), ConSecciones()),
                new JsonSerializerSettings { DateParseHandling = DateParseHandling.None });

            var seccion = json["item"][0]["item"];
            Assert.Equal(4, (int)seccion[0]["answer"][0]["valueInteger"]);
            Assert.Equal("2024-05-01", (string)seccion[1]["answer"][0]["valueDate"]);
            Assert.Equal("2024-05-01T10:30:00-03:00", (string)seccion[2]["answer"][0]["valueDateTime"]);
        }

        [Fact]
        public void DeclaraElPerfil()
        {
            var json = Serializar(Respuesta(new QuestionnaireResponseItem { LinkId = "x", ValueString = "y" }));

            Assert.Equal(FhirQuestionnaireResponseBuilder.ProfileUrl, (string)json["meta"]["profile"][0]);
        }

        [Fact]
        public void Parser_LeeLasPreguntasAnidadas()
        {
            var original = Respuesta(
                new QuestionnaireResponseItem { LinkId = "dolor", ValueDecimal = 4 },
                new QuestionnaireResponseItem { LinkId = "control", ValueDate = new DateTime(2024, 5, 1, 10, 30, 0) },
                new QuestionnaireResponseItem { LinkId = "obs", ValueString = "x" });
            var recurso = FhirQuestionnaireResponseBuilder.ToResource(original, ConSecciones());

            var leido = FhirQuestionnaireResponseParser.ToDomain(recurso).Items.ToList();

            Assert.Equal(new[] { "dolor", "control", "obs" }, leido.Select(i => i.LinkId));
            Assert.Equal(4, leido[0].ValueInteger);
            Assert.Equal(new DateTime(2024, 5, 1, 10, 30, 0), leido[1].ValueDate);
        }

        // ---- helpers ----

        private static QuestionnaireResponse Respuesta(params QuestionnaireResponseItem[] items)
        {
            var r = new QuestionnaireResponse
            {
                Id = Guid.NewGuid(),
                QuestionnaireId = Guid.NewGuid(),
                QuestionnaireVersionId = Guid.NewGuid(),
                PatientHospitalId = "PAC-000001",
                AuthorUserId = "dr.gomez",
                AuthoredAt = new DateTime(2026, 8, 1, 10, 30, 0, DateTimeKind.Utc),
                Status = ResponseStatus.InProgress
            };
            foreach (var i in items) r.Items.Add(i);
            return r;
        }

        private static QuestionnaireResponseItem Item(string linkId, string code)
            => new QuestionnaireResponseItem { LinkId = linkId, ValueCode = code };

        private static JObject Serializar(QuestionnaireResponse r, PatientDemographicsDto p = null)
            => JObject.Parse(FhirQuestionnaireResponseBuilder.Build(r, null, p));
    }
}
