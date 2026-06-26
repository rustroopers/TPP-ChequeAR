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

        public HospitalFhirClient(HttpClient http, string baseUrl, string bearerToken = null)
        {
            // No mutar Timeout/headers del HttpClient compartido: hacerlo tras la primera
            // request lanza InvalidOperationException (ver ServiceComposition). El timeout se
            // configura una sola vez en la composición; el bearer va por request, no en el cliente.
            _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            _baseUrl = baseUrl?.TrimEnd('/') ?? "";
            _bearerToken = bearerToken;
        }

        private readonly string _bearerToken;

        private HttpRequestMessage NewRequest(HttpMethod method, string url)
        {
            var req = new HttpRequestMessage(method, url);
            if (!string.IsNullOrEmpty(_bearerToken))
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _bearerToken);
            return req;
        }

        public async Task<string> GetPatientAsync(string hospitalId)
        {
            var res = await _http.SendAsync(NewRequest(HttpMethod.Get, _baseUrl + "/Patient/" + Uri.EscapeDataString(hospitalId)));
            if ((int)res.StatusCode == 404) return null;
            res.EnsureSuccessStatusCode();
            return await res.Content.ReadAsStringAsync();
        }

        public async Task<string> GetLastObservationAsync(string hospitalId, string code)
        {
            var url = $"{_baseUrl}/Observation?subject=Patient/{Uri.EscapeDataString(hospitalId)}&code={Uri.EscapeDataString(code)}&_sort=-date&_count=1";
            var res = await _http.SendAsync(NewRequest(HttpMethod.Get, url));
            res.EnsureSuccessStatusCode();
            return await res.Content.ReadAsStringAsync();
        }

        public async Task<string> SearchConditionsAsync(string hospitalId)
        {
            var url = $"{_baseUrl}/Condition?subject=Patient/{Uri.EscapeDataString(hospitalId)}";
            var res = await _http.SendAsync(NewRequest(HttpMethod.Get, url));
            res.EnsureSuccessStatusCode();
            return await res.Content.ReadAsStringAsync();
        }
    }
}
