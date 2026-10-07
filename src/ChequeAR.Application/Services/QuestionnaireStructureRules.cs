using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ChequeAR.Application.Fhir;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Application.Services
{
    /// <summary>
    /// Reglas de integridad de la estructura de un cuestionario que se pueden verificar sobre
    /// la entidad, antes de armar el recurso FHIR.
    ///
    /// Son dos niveles a propósito:
    ///  - <see cref="CheckForSave"/>: lo que corrompe datos aunque sea un borrador. Un linkId
    ///    repetido hace que dos preguntas compartan respuestas y columna de reporte.
    ///  - <see cref="CheckForPublish"/>: además, lo que vuelve inservible una versión congelada.
    ///    Una condición incompleta es normal mientras se edita (se eligió la pregunta pero
    ///    todavía no el valor) y no debe trabar el autoguardado, pero publicada es una
    ///    pregunta que nadie va a ver nunca.
    /// </summary>
    public static class QuestionnaireStructureRules
    {
        public static IReadOnlyList<string> CheckForSave(
            IEnumerable<QuestionGroup> groups, IEnumerable<Question> questions)
        {
            var errors = new List<string>();
            var links = (groups ?? Enumerable.Empty<QuestionGroup>()).Select(g => g.LinkId)
                .Concat((questions ?? Enumerable.Empty<Question>()).Select(q => q.LinkId))
                .Where(l => !string.IsNullOrWhiteSpace(l));

            foreach (var repetido in links.GroupBy(l => l, StringComparer.Ordinal)
                                          .Where(g => g.Count() > 1)
                                          .Select(g => g.Key))
            {
                errors.Add("Hay más de un elemento con el identificador '" + repetido
                         + "'. Cada pregunta y sección necesita un linkId único.");
            }
            return errors;
        }

        public static IReadOnlyList<string> CheckForPublish(
            IEnumerable<QuestionGroup> groups, IEnumerable<Question> questions)
        {
            var lista = (questions ?? Enumerable.Empty<Question>()).ToList();
            var errors = new List<string>(CheckForSave(groups, lista));

            var porLinkId = lista.Where(q => !string.IsNullOrWhiteSpace(q.LinkId))
                                 .GroupBy(q => q.LinkId, StringComparer.Ordinal)
                                 .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            foreach (var q in lista)
            {
                var cond = QuestionMetadataReader.ReadCondition(q.EnableWhenJson);
                if (cond == null) continue;

                var nombre = "La pregunta '" + (q.Text ?? q.LinkId) + "'";
                if (!porLinkId.TryGetValue(cond.Question, out var origen))
                {
                    errors.Add(nombre + " depende de '" + cond.Question + "', que no existe en el cuestionario.");
                    continue;
                }
                if (ReferenceEquals(origen, q))
                {
                    errors.Add(nombre + " no puede depender de sí misma.");
                    continue;
                }
                if (cond.Value == null)
                {
                    errors.Add(nombre + " tiene una condición sin valor esperado: completala o quitala.");
                    continue;
                }
                if (FhirQuestionnaireBuilder.ConditionAnswer(origen, cond.Value) != null) continue;

                var valor = Convert.ToString(cond.Value, CultureInfo.InvariantCulture);
                errors.Add(EsDeOpcion(origen)
                    ? nombre + " depende de la opción '" + valor + "', que ya no está entre las opciones de '" + (origen.Text ?? origen.LinkId) + "'."
                    : nombre + " compara '" + (origen.Text ?? origen.LinkId) + "' contra '" + valor + "', que no es un valor válido para esa pregunta.");
            }
            return errors;
        }

        private static bool EsDeOpcion(Question q)
            => q.Type == QuestionType.Choice || q.Type == QuestionType.OpenChoice;
    }
}
