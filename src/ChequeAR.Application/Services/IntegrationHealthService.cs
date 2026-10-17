using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;

namespace ChequeAR.Application.Services
{
    /// Pings external integrations and reports their health.
    /// Diseñado para alimentar el panel F1 (specs/006-frontend-ux/screens/03-fhir.md).
    public class IntegrationHealthService
    {
        private readonly IHospitalFhirClient _fhir;
        private readonly IOracleViewClient _oracle;
        private readonly string _hospitalAuthUrl;
        private readonly IHttpProbe _http;
        private readonly IFhirValidator _validator;

        public IntegrationHealthService(IHospitalFhirClient fhir, IOracleViewClient oracle, string hospitalAuthUrl,
            IHttpProbe http, IFhirValidator validator)
        {
            _fhir = fhir;
            _oracle = oracle;
            _hospitalAuthUrl = hospitalAuthUrl;
            _http = http;
            _validator = validator;
        }

        public async Task<IReadOnlyList<IntegrationStatus>> ProbeAllAsync()
        {
            var result = new List<IntegrationStatus>();
            result.Add(await ProbeFhirAsync());
            result.Add(await ProbeOracleAsync());
            result.Add(await ProbeAuthAsync());
            result.Add(ProbeValidator());
            return result;
        }

        private async Task<IntegrationStatus> ProbeFhirAsync()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                await _fhir.GetPatientAsync("__ping__");
                sw.Stop();
                return Ok("FHIR Hospital", sw.ElapsedMilliseconds);
            }
            catch (Exception ex) { return Down("FHIR Hospital", ex.Message); }
        }

        private async Task<IntegrationStatus> ProbeOracleAsync()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                await _oracle.PingAsync();
                sw.Stop();
                return Ok("Oracle Vistas", sw.ElapsedMilliseconds);
            }
            catch (Exception ex) { return Down("Oracle Vistas", ex.Message); }
        }

        private async Task<IntegrationStatus> ProbeAuthAsync()
        {
            if (string.IsNullOrEmpty(_hospitalAuthUrl))
                return new IntegrationStatus { Name = "Auth Hospital", Status = "unknown", Detail = "sin URL configurada" };
            var sw = Stopwatch.StartNew();
            try
            {
                // Cualquier respuesta HTTP (incluso 4xx) indica que el servicio está vivo.
                await _http.PingAsync(_hospitalAuthUrl);
                sw.Stop();
                return Ok("Auth Hospital", sw.ElapsedMilliseconds);
            }
            catch (Exception ex) { return Down("Auth Hospital", ex.Message); }
        }

        /// <summary>Valida un recurso mínimo: si el validador no carga sus perfiles, falla acá.</summary>
        private IntegrationStatus ProbeValidator()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                _validator.Validate("{\"resourceType\":\"Questionnaire\",\"status\":\"draft\"}");
                sw.Stop();
                return Ok("Validador FHIR", sw.ElapsedMilliseconds);
            }
            catch (Exception ex) { return Down("Validador FHIR", ex.Message); }
        }

        private static IntegrationStatus Ok(string name, long ms) => new IntegrationStatus
        { Name = name, Status = "ok", LastPingUtc = DateTime.UtcNow, LatencyMs = ms };

        private static IntegrationStatus Down(string name, string detail) => new IntegrationStatus
        { Name = name, Status = "down", LastPingUtc = DateTime.UtcNow, Detail = detail };
    }

    public class IntegrationStatus
    {
        public string Name { get; set; }
        public string Status { get; set; }  // ok | degraded | down | unknown
        public DateTime LastPingUtc { get; set; }
        public long LatencyMs { get; set; }
        public string Detail { get; set; }
    }
}
