using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Application.Services
{
    public enum QuestionChangeKind
    {
        Unchanged,
        Added,
        Removed,
        Modified
    }

    /// <summary>Un atributo de una pregunta que cambió entre dos versiones.</summary>
    public class QuestionFieldChange
    {
        /// <summary>
        /// Clave estable ("text", "type", "required", ...). La etiqueta legible la pone la
        /// vista: el diff no decide vocabulario de pantalla.
        /// </summary>
        public string Field { get; set; }
        public string Before { get; set; }
        public string After { get; set; }
    }

    public class QuestionDiff
    {
        public string LinkId { get; set; }
        public QuestionChangeKind Kind { get; set; }

        /// <summary>Enunciado vigente: el nuevo si existe, si no el que tenía.</summary>
        public string Text { get; set; }

        /// <summary>Sección en la que queda (o en la que estaba, si se quitó).</summary>
        public string Section { get; set; }

        public List<QuestionFieldChange> Changes { get; } = new List<QuestionFieldChange>();
    }

    public class QuestionnaireDiff
    {
        public string TitleBefore { get; set; }
        public string TitleAfter { get; set; }
        public bool TitleChanged => !string.Equals(TitleBefore, TitleAfter, StringComparison.Ordinal);

        public List<QuestionDiff> Questions { get; } = new List<QuestionDiff>();

        public int Count(QuestionChangeKind kind) => Questions.Count(q => q.Kind == kind);
        public bool HasChanges => TitleChanged || Questions.Any(q => q.Kind != QuestionChangeKind.Unchanged);
    }

    /// <summary>
    /// Compara dos versiones publicadas de un cuestionario.
    ///
    /// La identidad de una pregunta es su LinkId: es la clave con la que se guardan las
    /// respuestas y sobre la que pivotean los reportes, así que "la misma pregunta" en dos
    /// versiones es la que conserva el LinkId, aunque haya cambiado el enunciado. Un LinkId
    /// que desaparece y otro que aparece son una baja y un alta, no una edición: para quien
    /// analiza los datos son dos columnas distintas.
    ///
    /// Estático y puro: recibe dos entidades ya reconstruidas del FHIR congelado y no toca
    /// repositorios.
    /// </summary>
    public static class QuestionnaireVersionDiff
    {
        public static QuestionnaireDiff Compare(Questionnaire before, Questionnaire after)
        {
            before = before ?? new Questionnaire();
            after = after ?? new Questionnaire();

            var diff = new QuestionnaireDiff { TitleBefore = before.Title, TitleAfter = after.Title };

            var antes = Ordered(before);
            var despues = Ordered(after);
            var porLinkAntes = antes.GroupBy(x => x.Question.LinkId ?? "").ToDictionary(g => g.Key, g => g.First());
            var linksDespues = new HashSet<string>(despues.Select(x => x.Question.LinkId ?? ""));

            // Orden de salida: el de la versión nueva, y las quitadas al final. Así se lee
            // como el cuestionario que queda, con lo que se fue aparte.
            foreach (var n in despues)
            {
                var link = n.Question.LinkId ?? "";
                if (!porLinkAntes.TryGetValue(link, out var v))
                {
                    diff.Questions.Add(new QuestionDiff
                    {
                        LinkId = link, Kind = QuestionChangeKind.Added,
                        Text = n.Question.Text, Section = n.Section
                    });
                    continue;
                }

                var d = new QuestionDiff { LinkId = link, Text = n.Question.Text, Section = n.Section };
                Fields(v, n, d.Changes);
                d.Kind = d.Changes.Count == 0 ? QuestionChangeKind.Unchanged : QuestionChangeKind.Modified;
                diff.Questions.Add(d);
            }

            foreach (var v in antes.Where(x => !linksDespues.Contains(x.Question.LinkId ?? "")))
            {
                diff.Questions.Add(new QuestionDiff
                {
                    LinkId = v.Question.LinkId, Kind = QuestionChangeKind.Removed,
                    Text = v.Question.Text, Section = v.Section
                });
            }
            return diff;
        }

        private sealed class Located
        {
            public Question Question;
            public string Section;
        }

        private static List<Located> Ordered(Questionnaire q)
        {
            var grupos = (q.Groups ?? new List<QuestionGroup>()).ToDictionary(g => g.Id, g => g);
            QuestionGroup GrupoDe(Question x) =>
                x.GroupId.HasValue && grupos.TryGetValue(x.GroupId.Value, out var g) ? g : null;

            return (q.Questions ?? new List<Question>())
                .OrderBy(x => GrupoDe(x)?.OrderIndex ?? int.MaxValue)
                .ThenBy(x => x.OrderIndex)
                .Select(x => new Located { Question = x, Section = GrupoDe(x)?.Title })
                .ToList();
        }

        private static void Fields(Located antes, Located despues, List<QuestionFieldChange> changes)
        {
            var a = antes.Question;
            var b = despues.Question;

            Add(changes, "text", a.Text, b.Text);
            Add(changes, "section", antes.Section, despues.Section);
            Add(changes, "type", QuestionTypeMap.ToUi(a.Type), QuestionTypeMap.ToUi(b.Type));
            Add(changes, "required", YesNo(a.Required), YesNo(b.Required));

            var va = QuestionMetadataReader.ReadValidation(a.ValidationJson) ?? new QuestionValidation();
            var vb = QuestionMetadataReader.ReadValidation(b.ValidationJson) ?? new QuestionValidation();
            Add(changes, "unit", va.Unit, vb.Unit);
            Add(changes, "range", Range(va.Min, va.Max), Range(vb.Min, vb.Max));

            Add(changes, "options", Options(a), Options(b));
            Add(changes, "enableWhen", Condition(a.EnableWhenJson), Condition(b.EnableWhenJson));

            var ca = QuestionMetadataReader.ReadCoding(a.CodingJson);
            var cb = QuestionMetadataReader.ReadCoding(b.CodingJson);
            Add(changes, "code", Coding(ca), Coding(cb));
        }

        private static void Add(List<QuestionFieldChange> changes, string field, string before, string after)
        {
            var x = string.IsNullOrWhiteSpace(before) ? null : before.Trim();
            var y = string.IsNullOrWhiteSpace(after) ? null : after.Trim();
            if (string.Equals(x, y, StringComparison.Ordinal)) return;
            changes.Add(new QuestionFieldChange { Field = field, Before = x, After = y });
        }

        private static string YesNo(bool b) => b ? "Sí" : "No";

        private static string Range(decimal? min, decimal? max)
        {
            if (!min.HasValue && !max.HasValue) return null;
            var lo = min.HasValue ? min.Value.ToString(CultureInfo.InvariantCulture) : "−∞";
            var hi = max.HasValue ? max.Value.ToString(CultureInfo.InvariantCulture) : "∞";
            return lo + " – " + hi;
        }

        private static string Options(Question q)
        {
            var opts = (q.Options ?? new List<AnswerOption>())
                .OrderBy(o => o.OrderIndex)
                .Select(o => o.Display ?? o.Value ?? o.Code)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
            return opts.Count == 0 ? null : string.Join(" · ", opts);
        }

        private static string Condition(string json)
        {
            var c = QuestionMetadataReader.ReadCondition(json);
            if (c == null || string.IsNullOrWhiteSpace(c.Question)) return null;
            return c.Question + " " + (c.Op ?? "=") + " " + Convert.ToString(c.Value, CultureInfo.InvariantCulture);
        }

        private static string Coding(QuestionCoding c)
        {
            if (c == null || string.IsNullOrWhiteSpace(c.Code)) return null;
            return string.IsNullOrWhiteSpace(c.Display) ? c.Code : c.Code + " · " + c.Display;
        }
    }
}
