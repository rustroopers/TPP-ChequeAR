using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web;
using System.Web.Hosting;
using Newtonsoft.Json;

namespace ChequeAR.Api.Helpers
{
    /// Localizador simple basado en JSON.
    ///
    /// Carga `Content/i18n/{culture}.json` en memoria al primer uso por cultura.
    /// Resolución en cascada: cultura actual → es-AR → `[[key]]`.
    ///
    /// Documentado en specs/007-i18n/plan.md.
    public static class Cq
    {
        private const string DefaultCulture = "es-AR";
        private static readonly ConcurrentDictionary<string, Dictionary<string, string>> _cache
            = new ConcurrentDictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        /// Translate a key. Optional `params` object: anonymous type whose properties
        /// match `{name}` placeholders in the value.
        ///
        ///   Cq.T("home.greeting", new { name = "Ana" })
        public static string T(string key, object args = null)
        {
            if (string.IsNullOrEmpty(key)) return key;
            var culture = CurrentCulture();

            var dict = LoadCulture(culture) ?? LoadCulture(DefaultCulture);
            string text;
            if (dict == null || !dict.TryGetValue(key, out text))
            {
                // Fallback explícito a es-AR si la cultura activa no es esa.
                if (!string.Equals(culture, DefaultCulture, StringComparison.OrdinalIgnoreCase))
                {
                    var fallback = LoadCulture(DefaultCulture);
                    if (fallback != null && fallback.TryGetValue(key, out text)) { /* hit */ }
                    else text = null;
                }
            }
            if (string.IsNullOrEmpty(text)) return "[[" + key + "]]";
            if (args == null) return text;
            return ApplyArgs(text, args);
        }

        public static string CurrentCulture()
        {
            var ctx = HttpContext.Current;
            if (ctx != null)
            {
                var cookie = ctx.Request?.Cookies?["chequear_lang"];
                if (cookie != null && !string.IsNullOrEmpty(cookie.Value)) return cookie.Value;
            }
            return DefaultCulture;
        }

        public static IReadOnlyList<string> AvailableCultures()
        {
            var dir = HostingEnvironment.MapPath("~/Content/i18n/");
            if (dir == null || !Directory.Exists(dir)) return Array.Empty<string>();
            return Directory.GetFiles(dir, "*.json")
                            .Select(Path.GetFileNameWithoutExtension)
                            .OrderBy(x => x)
                            .ToList();
        }

        private static Dictionary<string, string> LoadCulture(string culture)
        {
            if (string.IsNullOrEmpty(culture)) return null;
            return _cache.GetOrAdd(culture, c =>
            {
                var path = HostingEnvironment.MapPath("~/Content/i18n/" + c + ".json");
                if (path == null || !File.Exists(path)) return null;
                try
                {
                    var json = File.ReadAllText(path);
                    var raw = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                    return raw ?? new Dictionary<string, string>();
                }
                catch
                {
                    return new Dictionary<string, string>();
                }
            });
        }

        private static string ApplyArgs(string text, object args)
        {
            if (args == null) return text;
            var props = args.GetType().GetProperties();
            var result = text;
            foreach (var p in props)
            {
                var value = p.GetValue(args)?.ToString() ?? string.Empty;
                result = result.Replace("{" + p.Name + "}", value);
            }
            return result;
        }
    }

    /// OWIN-side culture middleware: setea Thread.CurrentCulture / CurrentUICulture
    /// para que ASP.NET formatee fechas/números con la cultura correcta.
    public class CultureMiddleware : Microsoft.Owin.OwinMiddleware
    {
        public CultureMiddleware(Microsoft.Owin.OwinMiddleware next) : base(next) { }

        public override async System.Threading.Tasks.Task Invoke(Microsoft.Owin.IOwinContext context)
        {
            var lang = context.Request.Cookies["chequear_lang"];
            if (string.IsNullOrEmpty(lang)) lang = "es-AR";
            try
            {
                var culture = new System.Globalization.CultureInfo(lang);
                Thread.CurrentThread.CurrentCulture = culture;
                Thread.CurrentThread.CurrentUICulture = culture;
            }
            catch { /* ignore invalid culture code */ }
            await Next.Invoke(context);
        }
    }
}
