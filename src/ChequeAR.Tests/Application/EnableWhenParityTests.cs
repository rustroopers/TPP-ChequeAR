using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ChequeAR.Application.Fhir;
using ChequeAR.Application.Validation;
using ChequeAR.Domain.Entities;
using Jint;
using Newtonsoft.Json;
using Xunit;

namespace ChequeAR.Tests.Application
{
    /// <summary>
    /// El renderer (enable-when.js) y EnableWhenEvaluator tienen que ocultar las mismas
    /// preguntas: si no, $finalize borra respuestas que el médico vio o exige preguntas que no vio.
    /// </summary>
    public class EnableWhenParityTests
    {
        private static readonly Lazy<string> Script = new Lazy<string>(() => File.ReadAllText(ScriptPath()));

        public class Caso
        {
            public string Nombre { get; set; }
            public List<RenderableQuestion> Preguntas { get; set; } = new List<RenderableQuestion>();
            // Respuestas como las tiene el renderer: texto en los inputs, array en la múltiple.
            public Dictionary<string, object> Respuestas { get; set; } = new Dictionary<string, object>();
            public override string ToString() => Nombre;
        }

        private static RenderableQuestion P(string linkId, string type, string origen = null, string op = null, object valor = null)
            => new RenderableQuestion
            {
                LinkId = linkId, Type = type,
                EnableWhen = origen == null ? null : new RenderableCondition { Question = origen, Op = op, Value = valor }
            };

        private static Caso C(string nombre, Dictionary<string, object> respuestas, params RenderableQuestion[] preguntas)
            => new Caso { Nombre = nombre, Preguntas = preguntas.ToList(), Respuestas = respuestas };

        private static Dictionary<string, object> R(params object[] pares)
        {
            var d = new Dictionary<string, object>();
            for (var i = 0; i < pares.Length; i += 2) d[(string)pares[i]] = pares[i + 1];
            return d;
        }

        public static IEnumerable<object[]> Casos()
        {
            var bools = new[] { P("a", "boolean"), P("x", "string", "a", "=", true) };
            yield return new object[] { C("booleano sí", R("a", true), bools) };
            yield return new object[] { C("booleano no", R("a", false), bools) };
            yield return new object[] { C("booleano sin responder", R(), bools) };

            var mayor = new[] { P("n", "integer"), P("x", "string", "n", ">", 5L), P("y", "string", "n", "!=", 5L) };
            yield return new object[] { C("entero mayor", R("n", "7"), mayor) };
            yield return new object[] { C("entero menor", R("n", "3"), mayor) };
            yield return new object[] { C("entero igual", R("n", "5"), mayor) };
            yield return new object[] { C("entero sin responder", R(), mayor) };

            var dec = new[] { P("d", "decimal"), P("x", "string", "d", "=", 5.5m), P("y", "string", "d", "<", 6m) };
            yield return new object[] { C("decimal con coma", R("d", "5,5"), dec) };
            yield return new object[] { C("decimal distinto", R("d", "7"), dec) };

            var opcion = new[] { P("o", "choice"), P("x", "string", "o", "=", "a"), P("y", "string", "o", "!=", "a") };
            yield return new object[] { C("opción igual", R("o", "a"), opcion) };
            yield return new object[] { C("opción otra", R("o", "b"), opcion) };
            yield return new object[] { C("opción sin responder", R(), opcion) };

            var multiple = new[] { P("m", "openChoice"), P("x", "string", "m", "=", "a"), P("y", "string", "m", "!=", "a"), P("z", "string", "m", "!=", "c") };
            yield return new object[] { C("múltiple sin tocar", R(), multiple) };
            yield return new object[] { C("múltiple destildada", R("m", Array.Empty<string>()), multiple) };
            yield return new object[] { C("múltiple con a y b", R("m", new[] { "a", "b" }), multiple) };
            yield return new object[] { C("múltiple con b", R("m", new[] { "b" }), multiple) };

            var fecha = new[] { P("f", "date"), P("x", "string", "f", "=", "2024-05-01"), P("y", "string", "f", "!=", "2024-05-01") };
            yield return new object[] { C("fecha igual", R("f", "2024-05-01"), fecha) };
            yield return new object[] { C("fecha distinta", R("f", "2024-05-02"), fecha) };
            yield return new object[] { C("fecha sin responder", R(), fecha) };

            var momento = new[] { P("t", "dateTime"), P("x", "string", "t", "=", "2024-05-01T10:30") };
            yield return new object[] { C("fecha y hora igual", R("t", "2024-05-01T10:30"), momento) };
            yield return new object[] { C("fecha y hora distinta", R("t", "2024-05-01T11:00"), momento) };

            var cadena = new[]
            {
                P("a", "boolean"), P("b", "boolean", "a", "=", true),
                P("c", "string", "b", "=", true), P("d", "string", "b", "!=", true)
            };
            yield return new object[] { C("cadena con origen visible", R("a", true, "b", true), cadena) };
            yield return new object[] { C("cadena con origen oculta y valor viejo", R("a", false, "b", true), cadena) };

            var escala = new[] { P("e", "scale"), P("x", "string", "e", ">", 7L) };
            yield return new object[] { C("escala del slider", R("e", 8), escala) };
            yield return new object[] { C("entero retomado como número", R("n", 7), mayor) };
            yield return new object[] { C("entero tipeado con decimales en cero", R("n", "7.0"), mayor) };

            var ciclo = new[] { P("a", "boolean", "b", "=", true), P("b", "boolean", "a", "=", true) };
            yield return new object[] { C("ciclo", R("a", true), ciclo) };

            var multipleOculta = new[]
            {
                P("s", "boolean"), P("m", "openChoice", "s", "=", true), P("x", "string", "m", "!=", "a")
            };
            yield return new object[] { C("distinto sobre múltiple oculta", R("s", false, "m", new[] { "a" }), multipleOculta) };

            var sinValor = new[] { P("n", "integer"), P("x", "string", "n", ">", null), P("y", "string", "n", "!=", null), P("z", "string", "n", "exists", true) };
            yield return new object[] { C("valor esperado nulo y operador desconocido", R("n", "3"), sinValor) };
        }

        [Theory]
        [MemberData(nameof(Casos))]
        public void RendererYServidor_OcultanLasMismasPreguntas(Caso caso)
        {
            var estructura = new RenderableQuestionnaire();
            estructura.Groups.Add(new RenderableGroup { LinkId = "g", Questions = caso.Preguntas });

            var servidor = EnableWhenEvaluator.HiddenLinkIds(
                estructura, EnableWhenEvaluator.AnswersByLinkId(Items(caso), estructura));

            Assert.Equal(OcultasEnRenderer(caso).OrderBy(x => x), servidor.OrderBy(x => x));
        }

        private static List<string> OcultasEnRenderer(Caso caso)
        {
            var preguntas = JsonConvert.SerializeObject(caso.Preguntas.Select(q => new
            {
                linkId = q.LinkId, type = q.Type,
                enableWhen = q.EnableWhen == null ? null : new { question = q.EnableWhen.Question, op = q.EnableWhen.Op, value = q.EnableWhen.Value }
            }));
            var respuestas = JsonConvert.SerializeObject(caso.Respuestas);

            var engine = new Engine();
            engine.Execute(Script.Value);
            var json = engine.Evaluate(
                "(function () { var qs = " + preguntas + "; var a = " + respuestas + "; var by = {};" +
                " qs.forEach(function (q) { by[q.linkId] = q; });" +
                " return JSON.stringify(qs.filter(function (q) { return !ChequeAREnableWhen.isVisible(q, by, a); })" +
                ".map(function (q) { return q.linkId; })); })()").AsString();
            return JsonConvert.DeserializeObject<List<string>>(json);
        }

        /// <summary>Lo que el renderer manda al servidor para esas respuestas (toItems).</summary>
        private static List<QuestionnaireResponseItem> Items(Caso caso)
        {
            var items = new List<QuestionnaireResponseItem>();
            foreach (var par in caso.Respuestas)
            {
                var tipo = caso.Preguntas.First(q => q.LinkId == par.Key).Type;
                if (par.Value is string[] marcadas)
                {
                    items.AddRange(marcadas.Select(v => new QuestionnaireResponseItem { LinkId = par.Key, ValueCode = v }));
                    if (marcadas.Length == 0) items.Add(new QuestionnaireResponseItem { LinkId = par.Key });
                    continue;
                }
                var item = new QuestionnaireResponseItem { LinkId = par.Key };
                var texto = Convert.ToString(par.Value, CultureInfo.InvariantCulture);
                var numero = decimal.TryParse(texto.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : (decimal?)null;
                switch (tipo)
                {
                    case "boolean": item.ValueBoolean = (bool)par.Value; break;
                    case "integer": item.ValueInteger = (int?)numero; break;
                    case "decimal":
                    case "scale": item.ValueDecimal = numero; break;
                    case "date":
                    case "dateTime": item.ValueDate = DateTime.Parse(texto, CultureInfo.InvariantCulture); break;
                    case "choice": item.ValueCode = texto; break;
                    default: item.ValueString = texto; break;
                }
                items.Add(item);
            }
            return items;
        }

        private static string ScriptPath()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "ChequeAR.Api", "Scripts", "islands", "questionnaire-renderer", "enable-when.js");
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            throw new FileNotFoundException("No se encontró enable-when.js");
        }
    }
}
