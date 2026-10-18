using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Application.Reports
{
    /// <summary>
    /// Nombres de columna que se distinguen entre sí. Una variable reemplazada y la que la
    /// reemplaza son columnas distintas (cada una con su linkId), pero el Jefe suele dejar el
    /// mismo enunciado: sin esto el reporte, la exportación y la evolución mostrarían dos
    /// "Peso" sin forma de saber cuál es cuál.
    /// </summary>
    public static class ColumnLabels
    {
        /// <summary>
        /// Reescribe el texto de las preguntas de <paramref name="structure"/> (una copia armada
        /// por <see cref="PublishedStructure.Union"/>, no la entidad persistida).
        /// </summary>
        public static async Task ApplyAsync(Questionnaire structure, IClinicalVariableRepository variables)
        {
            var questions = structure?.Questions ?? new List<Question>();
            var ids = questions.Where(q => q.VariableId.HasValue).Select(q => q.VariableId.Value).Distinct().ToList();
            if (ids.Count == 0) return;

            var nombres = (await variables.GetManyAsync(ids)).ToDictionary(v => v.Id, v => v.Name);
            var sucesoras = (await variables.GetSuccessorsAsync(ids))
                .GroupBy(v => v.ReplacesId.Value)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(v => v.CreatedAt).First().Name);
            Apply(questions, nombres, sucesoras);
        }

        /// <param name="names">Nombre de cada variable, por id.</param>
        /// <param name="replacedBy">Nombre de la variable que reemplaza a cada una, por id.</param>
        public static void Apply(IEnumerable<Question> questions,
            IReadOnlyDictionary<Guid, string> names, IReadOnlyDictionary<Guid, string> replacedBy)
        {
            var lista = (questions ?? Enumerable.Empty<Question>()).ToList();
            foreach (var q in lista)
                if (q.VariableId.HasValue && replacedBy.TryGetValue(q.VariableId.Value, out var nueva))
                    q.Text = q.Text + " (reemplazada por " + nueva + ")";

            var repetidos = lista.GroupBy(q => (q.Text ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1);
            foreach (var grupo in repetidos)
                foreach (var q in grupo)
                    if (q.VariableId.HasValue && names.TryGetValue(q.VariableId.Value, out var nombre))
                        q.Text = q.Text + " (" + nombre + ")";
        }
    }
}
