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
            _http = http ?? new HttpClient();
            _baseUrl = baseUrl?.TrimEnd('/') ?? "";
            if (!string.IsNullOrEmpty(bearerToken))
                _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);
            _http.Timeout = TimeSpan.FromSeconds(5);
        }

        public async Task<string> GetPatientAsync(string hospitalId)
        {
            var res = await _http.GetAsync(_baseUrl + "/Patient/" + Uri.EscapeDataString(hospitalId));
            if ((int)res.StatusCode == 404) return null;
            res.EnsureSuccessStatusCode();
            return await res.Content.ReadAsStringAsync();
        }

        public async Task<string> GetLastObservationAsync(string hospitalId, string code)
        {
            var url = $"{_baseUrl}/Observation?subject=Patient/{Uri.EscapeDataString(hospitalId)}&code={Uri.EscapeDataString(code)}&_sort=-date&_count=1";
            var res = await _http.GetAsync(url);
            res.EnsureSuccessStatusCode();
            return await res.Content.ReadAsStringAsync();
        }

        public async Task<string> SearchConditionsAsync(string hospitalId)
        {
            var url = $"{_baseUrl}/Condition?subject=Patient/{Uri.EscapeDataString(hospitalId)}";
            var res = await _http.GetAsync(url);
            res.EnsureSuccessStatusCode();
            return await res.Content.ReadAsStringAsync();
        }
    }
}
