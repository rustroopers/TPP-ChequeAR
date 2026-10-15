using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ChequeAR.Application.Services
{
    /// <summary>Restricciones de una pregunta, ya desarmadas del contrato JSON.</summary>
    public class QuestionValidation
    {
        public decimal? Min { get; set; }
        public decimal? Max { get; set; }
        public string Unit { get; set; }
        public string Accept { get; set; }
        public int? MaxLength { get; set; }
        public string Pattern { get; set; }
    }

    /// <summary>Condición de visibilidad de una pregunta.</summary>
    public class QuestionCondition
    {
        public string Question { get; set; }
        public string Op { get; set; }

        /// <summary>
        /// Valor esperado tal como quedó guardado: bool, long, double o string. Null si la
        /// condición está incompleta (se eligió la pregunta pero todavía no el valor).
        /// </summary>
        public object Value { get; set; }
    }

    /// <summary>Codificación clínica y término de autoría.</summary>
    public class QuestionCoding
    {
        public string System { get; set; }
        public string Code { get; set; }
        public string Display { get; set; }

        /// <summary>Término para mostrar: el de autoría, o el display del concepto si no hay.</summary>
        public string Term { get; set; }

        /// <summary>Sólo el término que escribió quien diseñó la pregunta (null si no escribió ninguno).</summary>
        public string AuthoredTerm { get; set; }
    }

    /// <summary>
    /// Desarma los tres contratos JSON que la pregunta guarda como texto:
    /// ValidationJson, EnableWhenJson y CodingJson. Es el único lugar que conoce su forma:
    /// lo usan el builder FHIR, el diff de versiones y las pantallas.
    ///
    /// Tolerante a propósito: un JSON corrupto o un campo con un tipo inesperado (un "min"
    /// guardado como texto) se lee como ausente en vez de tirar. Una fila mal guardada no
    /// debería impedir ver ni publicar el resto del cuestionario.
    /// </summary>
    public static class QuestionMetadataReader
    {
        public static QuestionValidation ReadValidation(string json)
        {
            var o = TryParse(json);
            if (o == null) return null;

            return new QuestionValidation
            {
                Min = AsDecimal(o["min"]),
                Max = AsDecimal(o["max"]),
                Unit = AsString(o["unit"]),
                Accept = AsString(o["accept"]),
                MaxLength = AsInt(o["maxLength"]),
                Pattern = AsString(o["pattern"])
            };
        }

        public static QuestionCondition ReadCondition(string json)
        {
            var o = TryParse(json);
            var question = AsString(o?["question"]);
            if (string.IsNullOrWhiteSpace(question)) return null;

            var value = (o["value"] as JValue)?.Value;
            // El editor arranca una condición nueva con value = "": todavía no se eligió nada.
            if (value is string s && s.Length == 0) value = null;

            return new QuestionCondition
            {
                Question = question,
                Op = AsString(o["op"]) ?? "=",
                Value = value
            };
        }

        public static QuestionCoding ReadCoding(string json)
        {
            var o = TryParse(json);
            if (o == null) return null;

            var authored = AsString(o["term"]);
            return new QuestionCoding
            {
                System = AsString(o["system"]),
                Code = AsString(o["code"]),
                Display = AsString(o["display"]),
                AuthoredTerm = authored,
                // Una pregunta codificada sin término explícito se muestra con el display del
                // concepto elegido, que es la mejor aproximación disponible y evita que el
                // campo aparezca vacío al reabrirla.
                Term = authored ?? AsString(o["display"])
            };
        }

        /// <summary>
        /// Número de un valor JSON que puede venir como número o como texto ("12,5" incluido).
        /// Null si no hay un número legible.
        /// </summary>
        public static decimal? AsDecimal(JToken token)
        {
            switch (token?.Type)
            {
                case JTokenType.Integer:
                case JTokenType.Float:
                    try { return token.Value<decimal>(); }
                    catch (System.OverflowException) { return null; }
                case JTokenType.String:
                    return ParseDecimal((string)token);
                default:
                    return null;
            }
        }

        /// <summary>Igual que <see cref="AsDecimal"/> pero para un valor ya desarmado (bool, long, double, string).</summary>
        public static decimal? AsDecimal(object value)
        {
            switch (value)
            {
                case null: return null;
                case long l: return l;
                case int i: return i;
                case decimal d: return d;
                case double db:
                    if (double.IsNaN(db) || double.IsInfinity(db)) return null;
                    try { return (decimal)db; }
                    catch (System.OverflowException) { return null; }
                case string s: return ParseDecimal(s);
                default: return null;
            }
        }

        private static int? AsInt(JToken token)
        {
            var d = AsDecimal(token);
            if (!d.HasValue || d.Value != decimal.Truncate(d.Value)) return null;
            if (d.Value > int.MaxValue || d.Value < int.MinValue) return null;
            return (int)d.Value;
        }

        private static decimal? ParseDecimal(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            return decimal.TryParse(raw.Trim().Replace(',', '.'), NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : (decimal?)null;
        }

        private static string AsString(JToken token)
        {
            if (token == null) return null;
            switch (token.Type)
            {
                case JTokenType.String:
                case JTokenType.Integer:
                case JTokenType.Float:
                case JTokenType.Boolean:
                    return System.Convert.ToString(((JValue)token).Value, CultureInfo.InvariantCulture);
                default:
                    return null;
            }
        }

        private static JObject TryParse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            // Sin convertir fechas: una condición "2024-05-01" tiene que volver tal cual al editor.
            try { return JsonConvert.DeserializeObject<JObject>(json, new JsonSerializerSettings { DateParseHandling = DateParseHandling.None }); }
            catch (JsonException) { return null; }
        }
    }
}
