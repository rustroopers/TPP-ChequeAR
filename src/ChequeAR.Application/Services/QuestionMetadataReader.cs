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
        public object Value { get; set; }
    }

    /// <summary>Codificación clínica y término de autoría.</summary>
    public class QuestionCoding
    {
        public string System { get; set; }
        public string Code { get; set; }
        public string Display { get; set; }
        public string Term { get; set; }
    }

    /// <summary>
    /// Desarma los tres contratos JSON que la pregunta guarda como texto:
    /// ValidationJson, EnableWhenJson y CodingJson.
    ///
    /// Vivía en el controller MVC, que es la capa equivocada: la FORMA de esos contratos es
    /// conocimiento del dominio, y tenerla en presentación significaba que cualquier pantalla
    /// que necesitara leerlos la reimplementaba. El armado del ViewModel sí se queda allá:
    /// eso sí es presentación.
    ///
    /// Un JSON corrupto NO tumba la pantalla: se devuelve null y la pregunta se muestra sin
    /// restricciones. Una fila mal guardada no debería impedir ver el resto del cuestionario.
    /// </summary>
    public static class QuestionMetadataReader
    {
        public static QuestionValidation ReadValidation(string json)
        {
            var o = TryParse(json);
            if (o == null) return null;

            return new QuestionValidation
            {
                Min = (decimal?)o["min"],
                Max = (decimal?)o["max"],
                Unit = (string)o["unit"],
                Accept = (string)o["accept"],
                MaxLength = (int?)o["maxLength"],
                Pattern = (string)o["pattern"]
            };
        }

        public static QuestionCondition ReadCondition(string json)
        {
            var o = TryParse(json);
            if (o?["question"] == null) return null;

            return new QuestionCondition
            {
                Question = (string)o["question"],
                Op = (string)o["op"] ?? "=",
                Value = (o["value"] as JValue)?.Value
            };
        }

        public static QuestionCoding ReadCoding(string json)
        {
            var o = TryParse(json);
            if (o == null) return null;

            return new QuestionCoding
            {
                System = (string)o["system"],
                Code = (string)o["code"],
                Display = (string)o["display"],
                // Las preguntas codificadas antes de que existiera el término explícito no lo
                // tienen: se cae al display del concepto elegido, que es la mejor aproximación
                // disponible y evita que el campo aparezca vacío al reabrirlas.
                Term = (string)o["term"] ?? (string)o["display"]
            };
        }

        private static JObject TryParse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JObject.Parse(json); }
            catch (JsonException) { return null; }
        }
    }
}
