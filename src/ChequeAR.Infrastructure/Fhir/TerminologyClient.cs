using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading.Tasks;
using ChequeAR.Application.Exceptions;
using ChequeAR.Application.Interfaces;

namespace ChequeAR.Infrastructure.Fhir
{
    /// Proxy hacia un servidor terminológico FHIR (Snowstorm). Llama a la operación
    /// estándar ValueSet/$expand contra el value set implícito de SNOMED CT, opcionalmente
    /// acotado por ECL. Centralizarlo server-side evita exponer el server externo al
    /// navegador (CORS/licencia) y permite cambiar de instancia (test internacional ->
    /// nacional -> propio) tocando sólo la config.
    public class TerminologyClient : ITerminologyClient
    {
        private const string SnomedSystem = "http://snomed.info/sct";

        // El server público internacional filtra clientes que no parecen navegador
        // (responde denied.html) y limita por volumen (429). Un UA de navegador lo
        // destraba en dev. En un Snowstorm propio esto es inocuo.
        private const string BrowserUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36";

        // Caché en memoria compartida por todas las instancias del client (que es
        // transient): recorta llamadas repetidas al mismo término (retype/backspace,
        // preguntas iguales) y ayuda a no chocar contra el rate limit. TTL corto porque
        // el contenido cambia poco entre ediciones del release.
        private static readonly ConcurrentDictionary<string, CacheEntry> Cache =
            new ConcurrentDictionary<string, CacheEntry>();
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);
        private const int CacheMaxEntries = 500;

        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly string _ecl;
        private readonly string _displayLanguage;

        public TerminologyClient(HttpClient http, string baseUrl, string ecl = null, string displayLanguage = "es")
        {
            // No mutar Timeout/headers del HttpClient: puede ser compartido (ver
            // ServiceComposition). Todo lo por-request va en el HttpRequestMessage.
            _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            _baseUrl = baseUrl?.TrimEnd('/') ?? "";
            _ecl = ecl;
            _displayLanguage = string.IsNullOrWhiteSpace(displayLanguage) ? "es" : displayLanguage;
        }

        public async Task<string> ExpandSnomedAsync(string filter, int count)
        {
            if (string.IsNullOrWhiteSpace(_baseUrl))
                throw new TerminologyException(503, "Servidor terminológico no configurado (Terminology:BaseUrl).");
            if (count <= 0 || count > 50) count = 20;

            var key = _displayLanguage + "|" + count + "|" + (filter ?? "").Trim().ToLowerInvariant();
            if (Cache.TryGetValue(key, out var hit) && hit.ExpiresUtc > DateTime.UtcNow)
                return hit.Json;

            var vs = SnomedSystem + "?fhir_vs";
            if (!string.IsNullOrWhiteSpace(_ecl)) vs += "=ecl/" + _ecl;

            var url = _baseUrl + "/ValueSet/$expand"
                + "?url=" + Uri.EscapeDataString(vs)
                + "&filter=" + Uri.EscapeDataString(filter ?? "")
                + "&count=" + count
                + "&displayLanguage=" + Uri.EscapeDataString(_displayLanguage)
                + "&activeOnly=true";

            // Un solo reintento ante 429, honrando Retry-After (acotado). Más que eso sería
            // pelearle al rate limit; la respuesta correcta es mover a un server propio.
            var json = await SendAsync(url, allowRetry: true);

            var entry = new CacheEntry { Json = json, ExpiresUtc = DateTime.UtcNow.Add(CacheTtl) };
            Cache[key] = entry;
            if (Cache.Count > CacheMaxEntries) PruneCache();
            return json;
        }

        private async Task<string> SendAsync(string url, bool allowRetry)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/fhir+json"));
            req.Headers.TryAddWithoutValidation("User-Agent", BrowserUserAgent);

            using (var res = await _http.SendAsync(req))
            {
                if ((int)res.StatusCode == 429)
                {
                    var retryAfter = (int?)res.Headers.RetryAfter?.Delta?.TotalSeconds;
                    if (allowRetry)
                    {
                        // Espera acotada (máx 3s) para no colgar la request del usuario.
                        var wait = Math.Min(Math.Max(retryAfter ?? 1, 1), 3);
                        await Task.Delay(TimeSpan.FromSeconds(wait));
                        return await SendAsync(url, allowRetry: false);
                    }
                    throw new TerminologyException(429,
                        "El servidor SNOMED CT está limitando las consultas (429). Probá de nuevo en unos segundos.",
                        retryAfter);
                }

                if (!res.IsSuccessStatusCode)
                    throw new TerminologyException(502,
                        "El servidor SNOMED CT respondió " + (int)res.StatusCode + ".");

                return await res.Content.ReadAsStringAsync();
            }
        }

        private static void PruneCache()
        {
            // Barrido barato: saca lo vencido. Si aún así queda grande, no forzamos más
            // (la próxima expiración lo achica); el objetivo es evitar crecimiento sin techo.
            var now = DateTime.UtcNow;
            foreach (var kv in Cache)
                if (kv.Value.ExpiresUtc <= now)
                    Cache.TryRemove(kv.Key, out _);
        }

        private sealed class CacheEntry
        {
            public string Json;
            public DateTime ExpiresUtc;
        }
    }
}
