using System;
using System.Collections.Generic;
using System.Linq;
using ChequeAR.Domain.Entities;
using Newtonsoft.Json.Linq;

namespace ChequeAR.Application.Services
{
    /// <summary>
    /// Traduce una variable del catálogo a la pregunta que la usa.
    ///
    /// La pregunta guarda una COPIA de lo que la variable definía al insertarla, no una
    /// referencia que se resuelva en cada lectura. Dos razones:
    ///  - lo publicado tiene que quedar congelado, y QuestionnaireVersion.FhirJson sólo cubre
    ///    las versiones, no los borradores ni el GET que FhirQuestionnaireBuilder arma desde
    ///    la base;
    ///  - si la definición se resolviera al leer, editar una variable cambiaría el contenido
    ///    de un cuestionario sin mover su ConcurrencyVersion, o sea que el ETag mentiría.
    /// La copia sale de una versión publicada de la variable; Question.VariableVersionId y
    /// VariableRevision dicen de cuál, que es lo que permite avisar "hay una versión más nueva".
    ///
    /// Los dos JSON que emite respetan el contrato que ya lee QuestionMetadataReader, así que
    /// ni el builder ni el parser FHIR se enteran de que el origen del dato cambió.
    /// </summary>
    public static class ClinicalVariableSnapshot
    {
        /// <summary>
        /// Restricciones al contrato de Question.ValidationJson. Devuelve null -y no "{}"- si
        /// la variable no impone ninguna: es el mismo criterio del parser FHIR, y evita que la
        /// columna se llene de objetos vacíos que no significan nada.
        /// </summary>
        public static string ToValidationJson(ClinicalVariable v)
        {
            if (v == null) return null;
            var o = new JObject();
            if (v.Unit != null) o["unit"] = v.Unit;
            if (v.MinValue.HasValue) o["min"] = v.MinValue.Value;
            if (v.MaxValue.HasValue) o["max"] = v.MaxValue.Value;
            if (v.MaxLength.HasValue) o["maxLength"] = v.MaxLength.Value;
            if (v.Accept != null) o["accept"] = v.Accept;
            return o.Count == 0 ? null : o.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>
        /// Codificación al contrato de Question.CodingJson. También null si no hay nada: una
        /// variable puede existir antes de que se le encuentre el concepto SNOMED correcto.
        /// </summary>
        public static string ToCodingJson(ClinicalVariable v)
        {
            if (v == null) return null;
            var o = new JObject();
            if (v.SnomedCode != null)
            {
                if (v.SnomedSystem != null) o["system"] = v.SnomedSystem;
                o["code"] = v.SnomedCode;
                if (v.SnomedDisplay != null) o["display"] = v.SnomedDisplay;
            }
            if (v.Term != null) o["term"] = v.Term;
            return o.Count == 0 ? null : o.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>
        /// Copia a la pregunta todo lo que la variable manda, y deja intacto lo que el jefe sí
        /// puede decidir por cuestionario: Text, Required y EnableWhenJson (más LinkId,
        /// OrderIndex y GroupId, que son de la estructura).
        ///
        /// Pisa en silencio en vez de rechazar con 422 si el cliente mandó otro tipo u otra
        /// unidad: así el cliente no puede corromper nada y el editor no está obligado a
        /// reenviar campos que no controla.
        ///
        /// Text vacío cae al enunciado por defecto de la variable — es lo que hace que
        /// insertar "Peso" sin escribir nada quede como "Peso corporal" y no en blanco.
        /// </summary>
        public static void Apply(Question q, VariableDefinition definition, ClinicalVariableVersion version)
        {
            if (q == null || definition == null || version == null) return;

            var v = definition.ToVariable();
            q.VariableId = version.VariableId;
            q.VariableVersionId = version.Id;
            q.VariableRevision = version.VersionNumber;
            q.Type = v.Type;
            q.RepeatsAllowed = v.RepeatsAllowed;
            q.ValidationJson = ToValidationJson(v);
            q.CodingJson = ToCodingJson(v);

            if (string.IsNullOrWhiteSpace(q.Text)) q.Text = v.DefaultText ?? v.Name;

            q.Options = CopyOptions(v, q.Id);
        }

        private static List<AnswerOption> CopyOptions(ClinicalVariable v, Guid questionId)
        {
            var source = v.Options ?? new List<VariableOption>();
            return source
                .OrderBy(o => o.OrderIndex)
                .Select((o, i) => new AnswerOption
                {
                    Id = Guid.NewGuid(),
                    QuestionId = questionId,
                    Value = o.Value,
                    Display = o.Display,
                    Code = o.Code,
                    SnomedCode = o.SnomedCode,
                    SnomedDisplay = o.SnomedDisplay,
                    OrderIndex = i
                })
                .ToList();
        }
    }
}
