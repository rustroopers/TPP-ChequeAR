using System;
using System.Collections.Generic;
using System.Linq;
using ChequeAR.Application.Fhir;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Application.Services
{
    /// <summary>
    /// Los linkIds que ya usó alguna versión publicada, con la variable de cada uno. Reportes,
    /// evolución y re-anclaje unen respuestas por linkId: reusar uno para otra variable mezclaría
    /// datos de dos preguntas distintas en la misma columna.
    /// </summary>
    public static class PublishedLinkIds
    {
        public static Dictionary<string, Guid?> From(Questionnaire q)
        {
            var result = new Dictionary<string, Guid?>();
            foreach (var version in (q?.Versions ?? new List<QuestionnaireVersion>()).OrderByDescending(v => v.VersionNumber))
            {
                var frozen = FhirQuestionnaireReader.ReadDomain(version.FhirJson);
                foreach (var question in frozen?.Questions ?? new List<Question>())
                    if (!string.IsNullOrEmpty(question.LinkId) && !result.ContainsKey(question.LinkId))
                        result[question.LinkId] = question.VariableId;
            }
            return result;
        }

        /// <summary>Preguntas que reusan un linkId publicado con otra variable.</summary>
        public static List<string> Conflicts(IReadOnlyDictionary<string, Guid?> published, IEnumerable<Question> questions)
        {
            var errores = new List<string>();
            foreach (var question in questions ?? Enumerable.Empty<Question>())
            {
                if (question.LinkId == null || !published.TryGetValue(question.LinkId, out var anterior)) continue;
                if (anterior.HasValue && question.VariableId.HasValue && anterior != question.VariableId)
                    errores.Add("El identificador '" + question.LinkId + "' ya se usó en una versión publicada para otra variable.");
            }
            return errores;
        }
    }
}
