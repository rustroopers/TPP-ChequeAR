using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ChequeAR.Application.Services
{
    /// <summary>
    /// Lo que una versión publicada congela de una variable. Es la única forma de esa
    /// definición: la serializan la publicación y el backfill, y la aplica el snapshot de la
    /// pregunta.
    /// </summary>
    public class VariableDefinition
    {
        public Guid VariableId { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public string DefaultText { get; set; }
        public string Description { get; set; }
        public QuestionType Type { get; set; }
        public bool RepeatsAllowed { get; set; }
        public string Unit { get; set; }
        public decimal? MinValue { get; set; }
        public decimal? MaxValue { get; set; }
        public int? MaxLength { get; set; }
        public string Pattern { get; set; }
        public string Accept { get; set; }
        public string SnomedSystem { get; set; }
        public string SnomedCode { get; set; }
        public string SnomedDisplay { get; set; }
        public string Term { get; set; }
        public List<VariableDefinitionOption> Options { get; set; } = new List<VariableDefinitionOption>();

        public static VariableDefinition From(ClinicalVariable v) => new VariableDefinition
        {
            VariableId = v.Id,
            Code = v.Code,
            Name = v.Name,
            DefaultText = v.DefaultText,
            Description = v.Description,
            Type = v.Type,
            RepeatsAllowed = v.RepeatsAllowed,
            Unit = v.Unit,
            MinValue = v.MinValue,
            MaxValue = v.MaxValue,
            MaxLength = v.MaxLength,
            Pattern = v.Pattern,
            Accept = v.Accept,
            SnomedSystem = v.SnomedSystem,
            SnomedCode = v.SnomedCode,
            SnomedDisplay = v.SnomedDisplay,
            Term = v.Term,
            Options = (v.Options ?? new List<VariableOption>()).OrderBy(o => o.OrderIndex)
                .Select(o => new VariableDefinitionOption
                {
                    Value = o.Value,
                    Display = o.Display,
                    Code = o.Code,
                    SnomedSystem = o.SnomedSystem,
                    SnomedCode = o.SnomedCode,
                    SnomedDisplay = o.SnomedDisplay
                }).ToList()
        };

        /// <summary>
        /// La definición como variable, para reusar el armado del snapshot. No es una entidad
        /// para persistir.
        /// </summary>
        public ClinicalVariable ToVariable() => new ClinicalVariable
        {
            Id = VariableId,
            Code = Code,
            Name = Name,
            DefaultText = DefaultText,
            Description = Description,
            Type = Type,
            RepeatsAllowed = RepeatsAllowed,
            Unit = Unit,
            MinValue = MinValue,
            MaxValue = MaxValue,
            MaxLength = MaxLength,
            Pattern = Pattern,
            Accept = Accept,
            SnomedSystem = SnomedSystem,
            SnomedCode = SnomedCode,
            SnomedDisplay = SnomedDisplay,
            Term = Term,
            Options = Options.Select((o, i) => new VariableOption
            {
                VariableId = VariableId,
                Value = o.Value,
                Display = o.Display,
                Code = o.Code,
                SnomedSystem = o.SnomedSystem,
                SnomedCode = o.SnomedCode,
                SnomedDisplay = o.SnomedDisplay,
                OrderIndex = i
            }).ToList()
        };

        public string ToJson() => JsonConvert.SerializeObject(this, Settings);

        public static VariableDefinition Parse(string json)
            => string.IsNullOrWhiteSpace(json) ? null : JsonConvert.DeserializeObject<VariableDefinition>(json, Settings);

        /// <summary>
        /// Misma definición, comparando valores: un decimal leído de la base trae otra escala
        /// ("300.0000" contra "300") y como texto parecería un cambio.
        /// </summary>
        public bool SameAs(VariableDefinition other)
        {
            if (other == null) return false;
            return JToken.DeepEquals(Normalized(this), Normalized(other));
        }

        /// <summary>
        /// La pregunta tiene la misma definición que esta versión. Sirve para saber si una
        /// pregunta armada antes del versionado corresponde a la definición actual.
        /// </summary>
        public bool Matches(Question q)
        {
            if (q == null) return false;
            var v = ToVariable();
            if (q.Type != Type || q.RepeatsAllowed != RepeatsAllowed) return false;
            if (!SameJson(q.ValidationJson, ClinicalVariableSnapshot.ToValidationJson(v))) return false;
            if (!SameJson(q.CodingJson, ClinicalVariableSnapshot.ToCodingJson(v))) return false;

            var mine = Options.Select(o => new { o.Value, o.Display, o.Code, o.SnomedCode }).ToList();
            var theirs = (q.Options ?? new List<AnswerOption>()).OrderBy(o => o.OrderIndex)
                .Select(o => new { o.Value, o.Display, o.Code, o.SnomedCode }).ToList();
            return mine.SequenceEqual(theirs);
        }

        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            FloatParseHandling = FloatParseHandling.Decimal
        };

        private static JToken Normalized(VariableDefinition d)
        {
            var copy = Parse(d.ToJson());
            copy.MinValue = WithoutTrailingZeros(copy.MinValue);
            copy.MaxValue = WithoutTrailingZeros(copy.MaxValue);
            return JToken.FromObject(copy, JsonSerializer.Create(Settings));
        }

        private static decimal? WithoutTrailingZeros(decimal? value)
        {
            if (!value.HasValue) return null;
            return decimal.Parse(value.Value.ToString("G29", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }

        private static bool SameJson(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                return string.IsNullOrWhiteSpace(a) && string.IsNullOrWhiteSpace(b);
            try
            {
                return NumbersEqual(JToken.Parse(a), JToken.Parse(b));
            }
            catch (JsonException) { return false; }
        }

        private static bool NumbersEqual(JToken a, JToken b)
        {
            if (a is JObject oa && b is JObject ob)
            {
                var keys = oa.Properties().Select(p => p.Name).OrderBy(n => n).ToList();
                if (!keys.SequenceEqual(ob.Properties().Select(p => p.Name).OrderBy(n => n))) return false;
                return keys.All(k => NumbersEqual(oa[k], ob[k]));
            }
            if ((a.Type == JTokenType.Float || a.Type == JTokenType.Integer)
                && (b.Type == JTokenType.Float || b.Type == JTokenType.Integer))
                return a.Value<decimal>() == b.Value<decimal>();
            return JToken.DeepEquals(a, b);
        }
    }

    public class VariableDefinitionOption
    {
        public string Value { get; set; }
        public string Display { get; set; }
        public string Code { get; set; }
        public string SnomedSystem { get; set; }
        public string SnomedCode { get; set; }
        public string SnomedDisplay { get; set; }
    }
}
