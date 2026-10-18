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
    /// La definición de una variable, para compararla: lo clínico de una publicada no cambia,
    /// y la que la reemplaza tiene que distinguirse. También lee el JSON de las versiones que
    /// existían antes del reemplazo (VariableVersionRemoval).
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
        /// Misma definición clínica: ignora el enunciado por defecto y la descripción, que se
        /// corrigen en el lugar. Con <paramref name="ignoreIdentity"/> también ignora id, código y
        /// nombre: es la comparación entre una variable y la que la reemplaza.
        /// </summary>
        public bool ClinicallySameAs(VariableDefinition other, bool ignoreIdentity = false)
        {
            if (other == null) return false;
            return ClinicalPart(this, ignoreIdentity).SameAs(ClinicalPart(other, ignoreIdentity));
        }

        private static VariableDefinition ClinicalPart(VariableDefinition d, bool ignoreIdentity)
        {
            var copy = Parse(d.ToJson());
            copy.DefaultText = null;
            copy.Description = null;
            if (ignoreIdentity)
            {
                copy.VariableId = Guid.Empty;
                copy.Code = null;
                copy.Name = null;
            }
            return copy;
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
            var token = JToken.FromObject(copy, JsonSerializer.Create(Settings));
            // Un texto vacío es lo mismo que ninguno: el formulario manda "" donde lo
            // publicado quedó sin valor, y no es un cambio.
            foreach (var vacio in token.SelectTokens("$..*").OfType<JValue>()
                         .Where(v => v.Type == JTokenType.String && string.IsNullOrWhiteSpace((string)v.Value))
                         .Select(v => v.Parent).OfType<JProperty>().ToList())
                vacio.Remove();
            return token;
        }

        private static decimal? WithoutTrailingZeros(decimal? value)
        {
            if (!value.HasValue) return null;
            return decimal.Parse(value.Value.ToString("G29", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
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
