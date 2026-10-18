using System;
using System.Linq;
using Hl7.Fhir.Model;

namespace ChequeAR.Application.Fhir
{
    /// <summary>
    /// Las extensiones propias de ChequeAR. Existen porque hay información del dominio
    /// que no tiene lugar nativo en FHIR, y usar el mecanismo de extensión del estándar es
    /// justamente la forma correcta de transportarla sin inventar un formato paralelo.
    ///
    /// Antes de agregar una tercera: chequear que de verdad no exista campo nativo. Cada
    /// extensión propia es algo que un consumidor externo no va a entender sin leer nuestra
    /// documentación, así que se pagan de a una.
    /// </summary>
    public static class ChequeARExtensions
    {
        private const string Base = "https://chequear.garrahan/StructureDefinition/";

        /// <summary>
        /// Semilla clínica de autoría: el concepto en palabras que escribe quien diseña la
        /// pregunta, aunque todavía no haya elegido un código SNOMED.
        ///
        /// No es lo mismo que <c>item.code.display</c>: ése es el término preferido del
        /// concepto elegido; éste es lo que la persona quiso decir. Se guarda aunque no haya
        /// código, para poder retomar la codificación después.
        ///
        /// Hasta la feature 009 este dato no viajaba a FHIR (era metadata de autoría y el
        /// recurso publicado no la necesitaba). Con la interfaz hablando FHIR puro, sin esta
        /// extensión se perdería en CADA guardado.
        /// </summary>
        public const string QuestionTermUrl = Base + "question-term";

        /// <summary>
        /// El equivalente de <see cref="QuestionTermUrl"/> a nivel del cuestionario entero: el
        /// término en palabras que describe qué mide la ficha completa, escrito por quien la
        /// diseña, aunque todavía no haya elegido un concepto SNOMED. Igual que en la pregunta,
        /// no es lo mismo que <c>Questionnaire.code.display</c> (el término preferido del
        /// concepto elegido) y se guarda aunque no haya código, para poder codificar después.
        /// </summary>
        public const string QuestionnaireTermUrl = Base + "questionnaire-term";

        /// <summary>
        /// Identificador de la versión congelada a la que se ancla una ficha.
        ///
        /// FHIR permitiría referenciar por canonical versionado (Questionnaire/{id}|{n}), pero
        /// el dominio ancla por el Guid de QuestionnaireVersion, no por su número: traducir de
        /// ida y vuelta agregaría una búsqueda y un modo de fallo en cada lectura de ficha.
        /// </summary>
        public const string QuestionnaireVersionIdUrl = Base + "questionnaire-version-id";

        /// <summary>
        /// Variable del catálogo de la que proviene la pregunta.
        ///
        /// Todo lo que la variable define (tipo, unidad, rango, opciones, código) viaja
        /// denormalizado en el item, como manda el estándar: un consumidor externo lee el
        /// recurso completo sin saber que existe un catálogo. Lo que no tiene lugar nativo es
        /// la referencia en sí, y sin ella el par builder/parser la perdería en CADA guardado —
        /// el editor no podría decir de qué variable viene una pregunta, ni avisar cuando esa
        /// variable fue reemplazada.
        /// </summary>
        public const string QuestionVariableIdUrl = Base + "question-variable-id";

        /// <summary>
        /// Término SNOMED de una opción ("Moderate"). El display del Coding lleva la etiqueta que
        /// ve el médico ("Moderada").
        /// </summary>
        public const string OptionSnomedDisplayUrl = Base + "option-snomed-display";

        // ---- Acceso genérico ----

        public static void SetString(IExtendable target, string url, string value)
        {
            if (target == null) return;
            Remove(target, url);
            if (string.IsNullOrWhiteSpace(value)) return;
            target.Extension.Add(new Extension(url, new FhirString(value)));
        }

        public static string GetString(IExtendable target, string url)
        {
            return (Find(target, url)?.Value as FhirString)?.Value;
        }

        public static Extension Find(IExtendable target, string url)
        {
            if (target?.Extension == null) return null;
            return target.Extension.FirstOrDefault(e => e.Url == url);
        }

        private static void Remove(IExtendable target, string url)
        {
            if (target?.Extension == null) return;
            target.Extension.RemoveAll(e => e.Url == url);
        }

        // ---- Envoltorios por caso de uso ----

        public static void SetQuestionTerm(IExtendable item, string term)
            => SetString(item, QuestionTermUrl, term);

        public static string GetQuestionTerm(IExtendable item)
            => GetString(item, QuestionTermUrl);

        public static void SetQuestionnaireTerm(IExtendable resource, string term)
            => SetString(resource, QuestionnaireTermUrl, term);

        public static string GetQuestionnaireTerm(IExtendable resource)
            => GetString(resource, QuestionnaireTermUrl);

        public static void SetVariableId(IExtendable item, Guid? variableId)
        {
            if (!variableId.HasValue || variableId.Value == Guid.Empty) { SetString(item, QuestionVariableIdUrl, null); return; }
            SetString(item, QuestionVariableIdUrl, variableId.Value.ToString());
        }

        /// <summary>Null si la extensión falta o no contiene un Guid legible.</summary>
        public static Guid? GetVariableId(IExtendable item)
        {
            var raw = GetString(item, QuestionVariableIdUrl);
            if (string.IsNullOrWhiteSpace(raw)) return null;
            return Guid.TryParse(raw, out var parsed) ? parsed : (Guid?)null;
        }

        public static void SetQuestionnaireVersionId(IExtendable response, Guid versionId)
        {
            if (versionId == Guid.Empty) { SetString(response, QuestionnaireVersionIdUrl, null); return; }
            SetString(response, QuestionnaireVersionIdUrl, versionId.ToString());
        }

        /// <summary>Null si la extensión falta o no contiene un Guid legible.</summary>
        public static Guid? GetQuestionnaireVersionId(IExtendable response)
        {
            var raw = GetString(response, QuestionnaireVersionIdUrl);
            if (string.IsNullOrWhiteSpace(raw)) return null;
            return Guid.TryParse(raw, out var parsed) ? parsed : (Guid?)null;
        }
    }
}
