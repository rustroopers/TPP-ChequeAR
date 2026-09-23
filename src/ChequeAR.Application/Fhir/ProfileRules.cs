using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using FhirQuestionnaire = Hl7.Fhir.Model.Questionnaire;
using FhirResponse = Hl7.Fhir.Model.QuestionnaireResponse;

namespace ChequeAR.Application.Fhir
{
    /// <summary>
    /// Las reglas que los perfiles de conformidad de ChequeAR imponen por encima de FHIR R5.
    ///
    /// Viven en la capa de negocio, no en infraestructura, porque son decisiones del dominio:
    /// que un cuestionario sin especialidad no sirve, que un linkId duplicado corrompe fichas
    /// y cohortes, que una ficha sin anclaje de versión pierde su trazabilidad. Que se
    /// verifiquen al validar es un detalle de cuándo, no de dónde pertenecen — y acá se
    /// pueden probar sin levantar nada de infraestructura (FR-020, FR-021).
    ///
    /// El adaptador de infraestructura aporta lo que sí es suyo: el parseo estricto contra la
    /// especificación y la carga de los artefactos publicados.
    ///
    /// ⚠️ Estas reglas deben mantenerse en sincronía a mano con los StructureDefinition de
    /// ChequeAR.Infrastructure/Fhir/Profiles/. El motor que las interpretaría automáticamente
    /// (Firely.Fhir.Validation) exige netstandard2.1 y no corre sobre .NET Framework 4.7.1
    /// — ver research.md (R9).
    /// </summary>
    public static class ProfileRules
    {
        /// <summary>Tipos de item soportados si no se pudo cargar el ValueSet publicado.</summary>
        public static readonly IReadOnlyList<string> TiposPorOmision = new[]
        {
            "group", "string", "text", "integer", "decimal",
            "boolean", "date", "dateTime", "coding", "attachment"
        };

        public const string VersionAncladaUrl =
            "https://chequear.garrahan/StructureDefinition/questionnaire-version-id";

        /// <summary>Reglas del perfil ChequeARQuestionnaire.</summary>
        public static void CheckQuestionnaire(
            FhirQuestionnaire q,
            ICollection<string> errores,
            ICollection<string> avisos,
            ISet<string> tiposPermitidos = null)
        {
            if (q == null) { errores.Add("El recurso no es un cuestionario."); return; }

            var permitidos = tiposPermitidos ?? new HashSet<string>(TiposPorOmision);

            if (string.IsNullOrWhiteSpace(q.Title))
                errores.Add("El cuestionario no tiene título.");

            if (q.SubjectType == null || !q.SubjectType.Any(t => t == ResourceType.Patient))
                errores.Add("El cuestionario debe declarar subjectType = Patient.");

            if (q.UseContext == null || q.UseContext.Count == 0)
                errores.Add("El cuestionario no tiene especialidad asignada (useContext). "
                          + "Sin especialidad queda invisible en los filtros y en los reportes.");

            var items = Aplanar(q.Item).ToList();
            if (items.Count == 0) errores.Add("El cuestionario no tiene preguntas.");

            // Unicidad de linkId. Es el invariante que-2 de la especificación base, pero el
            // parser no evalúa invariantes: hay que verificarlo acá. Un duplicado corrompe
            // fichas y cohortes de reporte en silencio.
            foreach (var duplicado in items
                         .Where(i => !string.IsNullOrWhiteSpace(i.LinkId))
                         .GroupBy(i => i.LinkId)
                         .Where(g => g.Count() > 1)
                         .Select(g => g.Key))
            {
                errores.Add("Hay más de una pregunta con el identificador '" + duplicado
                          + "'. Los linkId deben ser únicos.");
            }

            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.LinkId))
                {
                    errores.Add("Hay una pregunta sin identificador (linkId).");
                    continue;
                }

                var tipo = item.TypeElement?.ObjectValue as string;
                if (string.IsNullOrWhiteSpace(tipo))
                    errores.Add("La pregunta '" + item.LinkId + "' no tiene tipo.");
                else if (!permitidos.Contains(tipo))
                    errores.Add("La pregunta '" + item.LinkId + "' usa el tipo '" + tipo
                              + "', que el sistema no sabe renderizar.");

                if (string.IsNullOrWhiteSpace(item.Text) && tipo != "group")
                    avisos.Add("La pregunta '" + item.LinkId + "' no tiene enunciado.");

                // Aviso y no error, deliberadamente. Que toda pregunta provenga del catálogo de
                // variables es una regla de NEGOCIO de ChequeAR, y la aplica QuestionnaireService
                // al guardar: un perfil FHIR describe la forma del recurso, no una restricción
                // sobre una tabla interna. Además un Questionnaire legítimo de otro sistema no
                // tiene por qué declarar un concepto, y rechazarlo acá rompería la lectura de
                // recursos externos que el resto de la capa sí sabe interpretar.
                if (tipo != "group" && (item.Code == null || item.Code.Count == 0))
                    avisos.Add("La pregunta '" + item.LinkId + "' no declara un concepto codificado, "
                             + "así que sus respuestas no van a ser comparables fuera de ChequeAR.");
            }

            // Una condición que apunta a una pregunta inexistente es una pregunta que nadie va
            // a ver nunca. Se detecta antes de publicar porque después la versión es inmutable
            // y ya no hay forma de corregirla.
            var conocidos = new HashSet<string>(
                items.Select(i => i.LinkId).Where(l => !string.IsNullOrWhiteSpace(l)));

            foreach (var item in items)
            {
                foreach (var cond in item.EnableWhen ?? new List<FhirQuestionnaire.EnableWhenComponent>())
                {
                    if (!string.IsNullOrWhiteSpace(cond.Question) && !conocidos.Contains(cond.Question))
                    {
                        errores.Add("La pregunta '" + item.LinkId + "' depende de '" + cond.Question
                                  + "', que no existe en el cuestionario.");
                    }
                }
            }
        }

        /// <summary>Reglas del perfil ChequeARQuestionnaireResponse. Todas sobre trazabilidad.</summary>
        public static void CheckResponse(FhirResponse r, ICollection<string> errores)
        {
            if (r == null) { errores.Add("El recurso no es una ficha."); return; }

            if (string.IsNullOrWhiteSpace(r.Questionnaire))
                errores.Add("La ficha no referencia ningún cuestionario.");

            if (string.IsNullOrWhiteSpace(r.Subject?.Identifier?.Value))
                errores.Add("La ficha no identifica al paciente (subject.identifier).");

            var anclaje = (r.Extension ?? new List<Extension>()).FirstOrDefault(e => e.Url == VersionAncladaUrl);
            if (anclaje == null || string.IsNullOrWhiteSpace((anclaje.Value as FhirString)?.Value))
            {
                errores.Add("La ficha no está anclada a una versión publicada. Sin el anclaje no "
                          + "hay garantía de que refleje las preguntas que se respondieron.");
            }
        }

        private static IEnumerable<FhirQuestionnaire.ItemComponent> Aplanar(
            IEnumerable<FhirQuestionnaire.ItemComponent> items)
        {
            foreach (var item in items ?? Enumerable.Empty<FhirQuestionnaire.ItemComponent>())
            {
                yield return item;
                foreach (var hija in Aplanar(item.Item)) yield return hija;
            }
        }
    }
}
