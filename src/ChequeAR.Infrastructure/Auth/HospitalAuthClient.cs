using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using ChequeAR.Application.Dtos;
using ChequeAR.Application.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
            var payload = JsonConvert.SerializeObject(new { username, password });
            using (var content = new StringContent(payload, Encoding.UTF8, "application/json"))
            using (var res = await _http.PostAsync(_loginUrl, content))
            {
                // 401, 423 (cuenta bloqueada) o cualquier otro: credenciales no válidas.
                if (!res.IsSuccessStatusCode) return null;
                return Parse(await res.Content.ReadAsStringAsync());
            }
        }

        /// <summary>
        /// Sin id o sin roles no hay sesión: antes se usaba el usuario tipeado y el rol Medico,
        /// y una respuesta incompleta del hospital terminaba dando acceso.
        /// </summary>
        public static UserInfo Parse(string body)
        {
            JObject json;
            try { json = JObject.Parse(body ?? string.Empty); }
            catch (JsonException) { Trace.TraceWarning("Login del hospital: respuesta que no es JSON."); return null; }

            var id = Scalar(json["id"]);
            var roles = List(json["roles"]);
            if (string.IsNullOrWhiteSpace(id) || roles == null || roles.Count == 0)
            {
                Trace.TraceWarning("Login del hospital: la respuesta no trae id o roles.");
                return null;
            }

            return new UserInfo
            {
                Id = id,
                DisplayName = Scalar(json["displayName"]) ?? id,
                Roles = roles,
                Specialties = List(json["specialties"]) ?? new List<string>()
            };
        }

        private static string Scalar(JToken token) =>
            token == null || token.Type == JTokenType.Null || token is JContainer ? null : token.ToString();

        private static List<string> List(JToken token) =>
            token is JArray array
                ? array.Select(Scalar).Where(s => !string.IsNullOrWhiteSpace(s)).ToList()
                : null;
    }
}
