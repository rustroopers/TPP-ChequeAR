using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using Newtonsoft.Json.Linq;
using FhirModel = Hl7.Fhir.Model;

namespace ChequeAR.Application.Services
{
    /// <summary>Un concepto sugerido por el servidor terminológico.</summary>
    public class CodeSuggestion
    {
        public string Code { get; set; }
        public string Display { get; set; }
    }

    /// <summary>
    /// Autocompletado de códigos SNOMED CT para el editor.
    ///
    /// Interpretar la respuesta del servidor terminológico estaba en el controller. Es
    /// traducción de un contrato externo al vocabulario del dominio: pertenece acá, y acá se
    /// puede probar sin levantar la capa web.
    /// </summary>
    public class TerminologyService
    {
        public const string SnomedSystem = "http://snomed.info/sct";

        /// <summary>
        /// Menos de tres caracteres da ruido y castiga al servidor externo: no se consulta.
        /// </summary>
        public const int MinimoCaracteres = 3;

        private readonly ITerminologyClient _client;

        public const int MaxCount = 50;

        public TerminologyService(ITerminologyClient client) { _client = client; }

        /// <summary>
        /// La respuesta de <c>ValueSet/$expand</c>: un ValueSet con la expansión sobre el
        /// ValueSet implícito de SNOMED CT entero.
        /// </summary>
        public async Task<FhirModel.ValueSet> ExpandAsync(string filter, int count, DateTime nowUtc)
        {
            count = Math.Max(1, Math.Min(count, MaxCount));
            var sugerencias = await SuggestAsync(filter, count);
            var expansion = new FhirModel.ValueSet.ExpansionComponent { TimestampElement = new FhirModel.FhirDateTime(new DateTimeOffset(nowUtc, TimeSpan.Zero)) };
            if (!string.IsNullOrWhiteSpace(filter))
                expansion.Parameter.Add(new FhirModel.ValueSet.ParameterComponent { Name = "filter", Value = new FhirModel.FhirString(filter.Trim()) });
            expansion.Parameter.Add(new FhirModel.ValueSet.ParameterComponent { Name = "count", Value = new FhirModel.Integer(count) });
            foreach (var s in sugerencias)
                expansion.Contains.Add(new FhirModel.ValueSet.ContainsComponent { System = SnomedSystem, Code = s.Code, Display = s.Display });
            return new FhirModel.ValueSet
            {
                Url = SnomedSystem + "?fhir_vs",
                Status = FhirModel.PublicationStatus.Active,
                Expansion = expansion
            };
        }

        public async Task<IReadOnlyList<CodeSuggestion>> SuggestAsync(string filter, int count)
        {
            if (string.IsNullOrWhiteSpace(filter) || filter.Trim().Length < MinimoCaracteres)
                return new List<CodeSuggestion>();

            var json = await _client.ExpandSnomedAsync(filter.Trim(), count);
            return Interpretar(json);
        }

        /// <summary>
        /// Lee la expansión del ValueSet. Un concepto sin código se descarta: no sirve para
        /// codificar nada y mostrarlo daría la impresión de que se puede elegir.
        /// </summary>
        public static IReadOnlyList<CodeSuggestion> Interpretar(string json)
        {
            var resultados = new List<CodeSuggestion>();
            if (string.IsNullOrWhiteSpace(json)) return resultados;

            JArray contains;
            try { contains = JObject.Parse(json)["expansion"]?["contains"] as JArray; }
            catch (Newtonsoft.Json.JsonException) { return resultados; }

            foreach (var c in contains ?? new JArray())
            {
                var code = (string)c["code"];
                if (string.IsNullOrEmpty(code)) continue;
                resultados.Add(new CodeSuggestion { Code = code, Display = (string)c["display"] });
            }
            return resultados;
        }
    }
}
