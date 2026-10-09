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

            // Una pregunta de opción sin opciones no se puede responder: con optionsOnly no hay
            // ni siquiera texto libre como salida.
            foreach (var q in lista.Where(x => EsDeOpcion(x) && (x.Options == null || x.Options.Count == 0)))
                errors.Add("La pregunta '" + (q.Text ?? q.LinkId) + "' es de opción pero no tiene opciones: nadie podría responderla.");

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
                var operadorInvalido = OperatorError(cond.Op, origen);
                if (operadorInvalido != null)
                {
                    errors.Add(nombre + " " + operadorInvalido);
                    continue;
                }
                if (FhirQuestionnaireBuilder.ConditionAnswer(origen, cond.Value) != null) continue;

                var valor = Convert.ToString(cond.Value, CultureInfo.InvariantCulture);
                errors.Add(EsDeOpcion(origen)
                    ? nombre + " depende de la opción '" + valor + "', que ya no está entre las opciones de '" + (origen.Text ?? origen.LinkId) + "'."
                    : nombre + " compara '" + (origen.Text ?? origen.LinkId) + "' contra '" + valor + "', que no es un valor válido para esa pregunta.");
            }

            errors.AddRange(CycleErrors(lista, porLinkId));
            return errors;
        }

        /// <summary>
        /// Los operadores que el renderer sabe evaluar (ver EnableWhenEvaluator.Compare): igual
        /// y distinto para cualquier tipo; mayor y menor sólo para números, que es lo único que
        /// el evaluador compara por orden. Un "&gt;" sobre una opción o un booleano daría siempre
        /// falso y la pregunta no se vería nunca. Null si el operador es válido.
        /// </summary>
        private static string OperatorError(string op, Question origen)
        {
            switch (op)
            {
                case "=":
                case "!=":
                    return null;
                case ">":
                case "<":
                    return EsNumerica(origen)
                        ? null
                        : "compara con '" + op + "' contra '" + (origen.Text ?? origen.LinkId)
                          + "', que no es numérica: sólo admite 'es igual a' o 'es distinto de'.";
                default:
                    return "usa el operador '" + op + "', que el sistema no sabe evaluar.";
            }
        }

        /// <summary>
        /// Condiciones que se encadenan en círculo (A depende de B y B de A, o más largo): para
        /// mostrar cualquiera hace falta haber respondido otra que tampoco se muestra, así que
        /// ninguna aparece nunca. El editor lo evita dejando condicionar sólo contra preguntas
        /// anteriores, pero un recurso que llega por la API no tiene esa garantía. La
        /// dependencia de sí misma ya se reporta aparte.
        /// </summary>
        private static IEnumerable<string> CycleErrors(List<Question> lista, Dictionary<string, Question> porLinkId)
        {
            var dependeDe = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var q in lista.Where(x => !string.IsNullOrWhiteSpace(x.LinkId)))
            {
                var cond = QuestionMetadataReader.ReadCondition(q.EnableWhenJson);
                if (cond != null && cond.Question != q.LinkId && porLinkId.ContainsKey(cond.Question))
                    dependeDe[q.LinkId] = cond.Question;
            }

            var reportados = new HashSet<string>(StringComparer.Ordinal);
            foreach (var inicio in dependeDe.Keys)
            {
                // Cada pregunta depende a lo sumo de una, así que basta con seguir la cadena.
                var camino = new List<string> { inicio };
                var actual = inicio;
                while (dependeDe.TryGetValue(actual, out var siguiente))
                {
                    var idx = camino.IndexOf(siguiente);
                    if (idx >= 0)
                    {
                        var ciclo = camino.Skip(idx).ToList();
                        var clave = string.Join("|", ciclo.OrderBy(x => x, StringComparer.Ordinal));
                        if (reportados.Add(clave))
                        {
                            var nombres = ciclo.Concat(new[] { ciclo[0] })
                                .Select(l => "'" + (porLinkId[l].Text ?? l) + "'");
                            yield return "Las condiciones de " + string.Join(" → ", nombres)
                                       + " forman un círculo: ninguna de esas preguntas se mostraría nunca.";
                        }
                        break;
                    }
                    camino.Add(siguiente);
                    actual = siguiente;
                }
            }
        }

        private static bool EsNumerica(Question q)
            => q.Type == QuestionType.Integer || q.Type == QuestionType.Decimal || q.Type == QuestionType.Scale;

        private static bool EsDeOpcion(Question q)
            => q.Type == QuestionType.Choice || q.Type == QuestionType.OpenChoice;
    }
}
