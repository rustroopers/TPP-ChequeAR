using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace ChequeAR.Application.Services
{
    /// <summary>
    /// Qué cambió en una edición, serializado en <c>AuditEvent.FhirJson</c>. Una edición que no
    /// cambia nada no deja evento. Los campos sueltos van como antes/después; la estructura de
    /// un cuestionario, como líneas quitadas y agregadas.
    /// </summary>
    public class AuditChangeSet
    {
        public List<AuditFieldChange> Changes { get; set; } = new List<AuditFieldChange>();
        public List<string> Removed { get; set; } = new List<string>();
        public List<string> Added { get; set; } = new List<string>();

        [JsonIgnore]
        public bool IsEmpty => Changes.Count == 0 && Removed.Count == 0 && Added.Count == 0;

        /// <summary>Los campos de <paramref name="after"/> cuyo valor difiere del de <paramref name="before"/>.</summary>
        public static AuditChangeSet Between(IDictionary<string, string> before, IDictionary<string, string> after)
        {
            return new AuditChangeSet
            {
                Changes = after.Keys
                    .Where(k => !string.Equals(Value(before, k), after[k] ?? "", StringComparison.Ordinal))
                    .Select(k => new AuditFieldChange { Field = k, Before = before.TryGetValue(k, out var a) ? a : null, After = after[k] })
                    .ToList()
            };
        }

        /// <summary>Líneas que están sólo antes y sólo después, en su orden.</summary>
        public void AddLineDiff(IList<string> before, IList<string> after)
        {
            var antes = new HashSet<string>(before);
            var despues = new HashSet<string>(after);
            Removed.AddRange(before.Where(l => !despues.Contains(l)));
            Added.AddRange(after.Where(l => !antes.Contains(l)));
        }

        public string ToJson() => JsonConvert.SerializeObject(this);

        /// <summary>Null si el evento no trae cambios interpretables (eventos viejos u otro formato).</summary>
        public static AuditChangeSet Read(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var set = JsonConvert.DeserializeObject<AuditChangeSet>(json);
                return set == null || set.IsEmpty ? null : set;
            }
            catch (JsonException) { return null; }
        }

        private static string Value(IDictionary<string, string> d, string key)
            => d.TryGetValue(key, out var v) ? v ?? "" : "";
    }

    public class AuditFieldChange
    {
        public string Field { get; set; }
        public string Before { get; set; }
        public string After { get; set; }
    }
}
