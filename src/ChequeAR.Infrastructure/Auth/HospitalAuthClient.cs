using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using ChequeAR.Application.Dtos;
using ChequeAR.Application.Interfaces;

namespace ChequeAR.Infrastructure.Auth
{
    public class HospitalAuthClient : ICredentialAuthenticator
    {
        private readonly HttpClient _http;
        private readonly string _loginUrl;

        public HospitalAuthClient(HttpClient http, string loginUrl)
        {
            // No mutar Timeout/headers: el HttpClient puede ser compartido entre clientes
            // (ver ServiceComposition). Modificarlo tras la primera request lanza
            // InvalidOperationException. El timeout se configura una sola vez en la composición.
            _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            _loginUrl = loginUrl;
        }

        public async Task<UserInfo> LoginAsync(string username, string password)
        {
            // Estructura mínima esperada. En producción reemplazar por el contrato real del hospital.
            var payload = "{\"username\":\"" + Escape(username) + "\",\"password\":\"" + Escape(password) + "\"}";
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var res = await _http.PostAsync(_loginUrl, content);
            if (!res.IsSuccessStatusCode) return null;
            var body = await res.Content.ReadAsStringAsync();

            // Parser muy simple para MVP. Producción: Newtonsoft.Json o System.Text.Json.
            return new UserInfo
            {
                Id = ExtractScalar(body, "id") ?? username,
                DisplayName = ExtractScalar(body, "displayName") ?? username,
                Roles = ExtractList(body, "roles") ?? new List<string> { "Medico" },
                Specialties = ExtractList(body, "specialties") ?? new List<string>()
            };
        }

        private static string Escape(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static string ExtractScalar(string json, string key)
        {
            var marker = "\"" + key + "\"";
            var idx = json.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0) return null;
            var colon = json.IndexOf(':', idx);
            var quote = json.IndexOf('"', colon + 1);
            if (quote < 0) return null;
            var end = json.IndexOf('"', quote + 1);
            return end > quote ? json.Substring(quote + 1, end - quote - 1) : null;
        }

        private static List<string> ExtractList(string json, string key)
        {
            var marker = "\"" + key + "\"";
            var idx = json.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0) return null;
            var open = json.IndexOf('[', idx);
            var close = json.IndexOf(']', open);
            if (open < 0 || close < 0) return null;
            var inside = json.Substring(open + 1, close - open - 1);
            var items = new List<string>();
            foreach (var part in inside.Split(','))
            {
                var trimmed = part.Trim().Trim('"');
                if (!string.IsNullOrEmpty(trimmed)) items.Add(trimmed);
            }
            return items;
        }
    }
}
