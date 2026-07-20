using System;
using System.Net.Http;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;

namespace ChequeAR.Infrastructure.Fhir
{
    public class HospitalFhirClient : IHospitalFhirClient
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly string _bearerToken;

        public HospitalFhirClient(HttpClient http, string baseUrl, string bearerToken = null)
        {
            // El HttpClient puede ser compartido (singleton): NO se mutan acá sus
            // propiedades (Timeout/DefaultRequestHeaders). Tras la primera request eso
            // lanzaría InvalidOperationException. El Timeout se configura una sola vez
            // al crear el client (ver ServiceComposition); el bearer se aplica por request.
            _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            _baseUrl = baseUrl?.TrimEnd('/') ?? "";
            _bearerToken = bearerToken;
        }

        public async Task<string> GetPatientAsync(string hospitalId)
        {
            using (var res = await SendGetAsync(_baseUrl + "/Patient/" + Uri.EscapeDataString(hospitalId)))
            {
                if ((int)res.StatusCode == 404) return null;
                res.EnsureSuccessStatusCode();
                return await res.Content.ReadAsStringAsync();
            }
        }

        public async Task<string> GetLastObservationAsync(string hospitalId, string code)
        {
            var url = $"{_baseUrl}/Observation?subject=Patient/{Uri.EscapeDataString(hospitalId)}&code={Uri.EscapeDataString(code)}&_sort=-date&_count=1";
            using (var res = await SendGetAsync(url))
            {
                res.EnsureSuccessStatusCode();
                return await res.Content.ReadAsStringAsync();
            }
        }

        public async Task<string> SearchConditionsAsync(string hospitalId)
        {
            var url = $"{_baseUrl}/Condition?subject=Patient/{Uri.EscapeDataString(hospitalId)}";
            using (var res = await SendGetAsync(url))
            {
                res.EnsureSuccessStatusCode();
                return await res.Content.ReadAsStringAsync();
            }
        }

        private Task<HttpResponseMessage> SendGetAsync(string url)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(_bearerToken))
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _bearerToken);
            return _http.SendAsync(req);
        }
    }
}
