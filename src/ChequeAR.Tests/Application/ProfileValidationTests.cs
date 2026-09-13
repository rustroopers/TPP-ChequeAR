using System;
using System.Collections.Generic;
using System.Linq;
using ChequeAR.Application.Fhir;
using Hl7.Fhir.Model;
using Xunit;
using FhirQuestionnaire = Hl7.Fhir.Model.Questionnaire;
using FhirResponse = Hl7.Fhir.Model.QuestionnaireResponse;

namespace ChequeAR.Tests.Application
{
    /// <summary>
    /// Las reglas de conformidad propias, probadas SIN levantar infraestructura: no hacen
    /// falta la especificación de 10 MB ni los archivos de perfil para verificar que un
    /// cuestionario sin especialidad no se publica. Que se puedan probar así es la razón por
    /// la que viven en la capa de negocio y no en el adaptador (FR-020, FR-021).
    /// </summary>
    public class ProfileValidationTests
    {
        // ─────────────────────────────── Cuestionario ───────────────────────────────

        [Fact]
        public void CuestionarioCorrecto_NoTieneErrores()
        {
            Assert.Empty(ErroresDe(Valido()));
        }

        [Fact]
        public void SinTitulo_Falla()
        {
            var q = Valido();
            q.Title = null;

            Assert.Contains(ErroresDe(q), e => e.Contains("título"));
        }

        /// <summary>
        /// La especialidad gobierna filtros, permisos y alcance de reportes. Un cuestionario
        /// sin ella queda invisible justo para quien debería usarlo.
        /// </summary>
        [Fact]
        public void SinEspecialidad_Falla()
        {
            var q = Valido();
            q.UseContext.Clear();

            Assert.Contains(ErroresDe(q), e => e.Contains("especialidad"));
        }

        [Fact]
        public void SinSubjectTypePatient_Falla()
        {
            var q = Valido();
            q.SubjectType = Array.Empty<ResourceType?>();

            Assert.Contains(ErroresDe(q), e => e.Contains("subjectType"));
        }

        /// <summary>
        /// Un linkId duplicado corrompe fichas y cohortes de reporte en silencio: las
        /// respuestas referencian por linkId y no hay forma de saber a cuál pregunta iban.
        /// </summary>
        [Fact]
        public void LinkIdDuplicado_Falla()
        {
            var q = Valido();
            q.Item[0].Item.Add(Pregunta("peso", "Peso otra vez", FhirQuestionnaire.QuestionnaireItemType.Decimal));

            var errores = ErroresDe(q);

            Assert.Contains(errores, e => e.Contains("peso") && e.Contains("únicos"));
        }

        [Fact]
        public void LinkIdDuplicadoEnSeccionesDistintas_TambienFalla()
        {
            // El chequeo tiene que ser sobre el árbol aplanado, no por sección.
            var q = Valido();
            var otra = new FhirQuestionnaire.ItemComponent
            {
                LinkId = "seccion-2",
                Text = "Otra sección",
                Type = FhirQuestionnaire.QuestionnaireItemType.Group
            };
            otra.Item.Add(Pregunta("peso", "Peso", FhirQuestionnaire.QuestionnaireItemType.Decimal));
            q.Item.Add(otra);

            Assert.Contains(ErroresDe(q), e => e.Contains("peso") && e.Contains("únicos"));
        }

        [Fact]
        public void PreguntaSinLinkId_Falla()
        {
            var q = Valido();
            q.Item[0].Item.Add(Pregunta(null, "Sin identificador", FhirQuestionnaire.QuestionnaireItemType.String));

            Assert.Contains(ErroresDe(q), e => e.Contains("sin identificador"));
        }

        [Fact]
        public void SinPreguntas_Falla()
        {
            var q = Valido();
            q.Item.Clear();

            Assert.Contains(ErroresDe(q), e => e.Contains("no tiene preguntas"));
        }

        /// <summary>
        /// Un tipo fuera del conjunto soportado es una pregunta que se puede publicar pero no
        /// responder: el renderizador no sabe dibujarla.
        /// </summary>
        [Fact]
        public void TipoNoSoportado_Falla()
        {
            var q = Valido();
            q.Item[0].Item.Add(Pregunta("ref", "Referencia", FhirQuestionnaire.QuestionnaireItemType.Reference));

            Assert.Contains(ErroresDe(q), e => e.Contains("ref") && e.Contains("no sabe renderizar"));
        }

        /// <summary>
        /// Una condición que apunta a una pregunta inexistente produce una pregunta que nadie
        /// va a ver nunca. Hay que detectarla ANTES de publicar: después la versión es
        /// inmutable y ya no se puede corregir.
        /// </summary>
        [Fact]
        public void CondicionQueApuntaAPreguntaInexistente_Falla()
        {
            var q = Valido();
            var dependiente = Pregunta("detalle", "Detalle", FhirQuestionnaire.QuestionnaireItemType.String);
            dependiente.EnableWhen.Add(new FhirQuestionnaire.EnableWhenComponent
            {
                Question = "no-existe",
                Operator = FhirQuestionnaire.QuestionnaireItemOperator.Equal,
                Answer = new FhirBoolean(true)
            });
            q.Item[0].Item.Add(dependiente);

            Assert.Contains(ErroresDe(q), e => e.Contains("no-existe") && e.Contains("no existe"));
        }

        [Fact]
        public void PreguntaSinEnunciado_EsAvisoNoError()
        {
            // Publicar sin enunciado es raro pero no rompe nada: se avisa, no se bloquea.
            var q = Valido();
            q.Item[0].Item[0].Text = null;

            var errores = new List<string>();
            var avisos = new List<string>();
            ProfileRules.CheckQuestionnaire(q, errores, avisos);

            Assert.Empty(errores);
            Assert.Contains(avisos, a => a.Contains("enunciado"));
        }

        // ───────────────────────────────── Ficha ─────────────────────────────────

        [Fact]
        public void FichaCorrecta_NoTieneErrores()
        {
            Assert.Empty(ErroresDe(FichaValida()));
        }

        [Fact]
        public void FichaSinPaciente_Falla()
        {
            var r = FichaValida();
            r.Subject = null;

            Assert.Contains(ErroresDe(r), e => e.Contains("paciente"));
        }

        [Fact]
        public void FichaSinCuestionario_Falla()
        {
            var r = FichaValida();
            r.Questionnaire = null;

            Assert.Contains(ErroresDe(r), e => e.Contains("cuestionario"));
        }

        /// <summary>
        /// Sin anclaje de versión no hay garantía de que la ficha refleje las preguntas que el
        /// médico leyó: una publicación posterior la reinterpretaría.
        /// </summary>
        [Fact]
        public void FichaSinAnclajeDeVersion_Falla()
        {
            var r = FichaValida();
            r.Extension.Clear();

            Assert.Contains(ErroresDe(r), e => e.Contains("anclada"));
        }

        // ─────────────────────────────── constructores ───────────────────────────────

        private static FhirQuestionnaire Valido()
        {
            var q = new FhirQuestionnaire
            {
                Title = "Control cardiológico",
                Status = PublicationStatus.Active,
                SubjectType = new ResourceType?[] { ResourceType.Patient }
            };
            q.UseContext.Add(new UsageContext
            {
                Code = new Coding("http://terminology.hl7.org/CodeSystem/usage-context-type", "focus"),
                Value = new CodeableConcept("https://chequear.garrahan/CodeSystem/specialty", "cardio")
            });

            var seccion = new FhirQuestionnaire.ItemComponent
            {
                LinkId = "seccion-1",
                Text = "Antecedentes",
                Type = FhirQuestionnaire.QuestionnaireItemType.Group
            };
            seccion.Item.Add(Pregunta("peso", "Peso (kg)", FhirQuestionnaire.QuestionnaireItemType.Decimal));
            q.Item.Add(seccion);
            return q;
        }

        private static FhirQuestionnaire.ItemComponent Pregunta(
            string linkId, string text, FhirQuestionnaire.QuestionnaireItemType tipo)
            => new FhirQuestionnaire.ItemComponent { LinkId = linkId, Text = text, Type = tipo };

        private static FhirResponse FichaValida()
        {
            var r = new FhirResponse
            {
                Questionnaire = "https://chequear.garrahan/Questionnaire/" + System.Guid.NewGuid(),
                Status = FhirResponse.QuestionnaireResponseStatus.InProgress,
                Subject = new ResourceReference
                {
                    Identifier = new Identifier("https://chequear.garrahan/patient-hospital-id", "PAC-000001")
                }
            };
            ChequeARExtensions.SetQuestionnaireVersionId(r, System.Guid.NewGuid());
            return r;
        }

        private static List<string> ErroresDe(FhirQuestionnaire q)
        {
            var errores = new List<string>();
            ProfileRules.CheckQuestionnaire(q, errores, new List<string>());
            return errores;
        }

        private static List<string> ErroresDe(FhirResponse r)
        {
            var errores = new List<string>();
            ProfileRules.CheckResponse(r, errores);
            return errores;
        }
    }
}
