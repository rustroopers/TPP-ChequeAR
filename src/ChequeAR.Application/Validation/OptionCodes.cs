using System.Collections.Generic;
using System.Linq;
using ChequeAR.Application.Fhir;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Application.Validation
{
    /// <summary>
    /// Lleva el ValueCode de las respuestas de opción al código de la opción. Hasta que el
    /// renderer pasó a guardar el código se guardaba el texto visible, y una ficha retomada
    /// o re-anclada todavía puede traerlo.
    /// </summary>
    public static class OptionCodes
    {
        /// <returns>Cuántos ítems cambiaron.</returns>
        public static int Normalize(RenderableQuestionnaire structure, IEnumerable<QuestionnaireResponseItem> items)
        {
            if (structure == null || items == null) return 0;

            var byLinkId = ChoiceQuestions(structure);
            var changed = 0;
            foreach (var item in items)
            {
                if (item == null || string.IsNullOrEmpty(item.ValueCode)) continue;
                if (!byLinkId.TryGetValue(item.LinkId ?? string.Empty, out var question)) continue;
                var option = question.FindOption(item.ValueCode);
                if (option == null || option.Code == item.ValueCode) continue;
                item.ValueCode = option.Code;
                changed++;
            }
            return changed;
        }

        /// <summary>
        /// Al pasar una ficha de versión, lleva cada respuesta al código que tiene la misma
        /// opción en la versión nueva, aunque haya cambiado el código o la etiqueta (no los dos).
        /// </summary>
        public static int Carry(RenderableQuestionnaire from, RenderableQuestionnaire to, IEnumerable<QuestionnaireResponseItem> items)
        {
            if (items == null) return 0;
            var list = items.ToList();
            var changed = Normalize(from, list);
            if (from == null || to == null) return changed + Normalize(to, list);

            var oldByLinkId = ChoiceQuestions(from);
            var newByLinkId = ChoiceQuestions(to);
            foreach (var item in list)
            {
                if (item == null || string.IsNullOrEmpty(item.ValueCode)) continue;
                if (!newByLinkId.TryGetValue(item.LinkId ?? string.Empty, out var target)) continue;
                if (target.FindOption(item.ValueCode) != null) continue;

                oldByLinkId.TryGetValue(item.LinkId, out var source);
                var old = source?.FindOption(item.ValueCode);
                if (old == null) continue;
                var match = new[] { old.Display }.Concat(old.Aliases)
                    .Select(target.FindOption).FirstOrDefault(o => o != null);
                if (match == null) continue;
                item.ValueCode = match.Code;
                changed++;
            }
            return changed + Normalize(to, list);
        }

        private static Dictionary<string, RenderableQuestion> ChoiceQuestions(RenderableQuestionnaire structure)
        {
            var byLinkId = new Dictionary<string, RenderableQuestion>();
            foreach (var q in structure?.Groups.SelectMany(g => g.Questions) ?? Enumerable.Empty<RenderableQuestion>())
                if (q.LinkId != null && q.Options.Count > 0 && !byLinkId.ContainsKey(q.LinkId)) byLinkId[q.LinkId] = q;
            return byLinkId;
        }
    }
}
