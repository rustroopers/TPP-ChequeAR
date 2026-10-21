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
        private readonly IFhirValidator _validator;
        private readonly IOperationalLog _log;

        public IntegrationHealthService(IHospitalFhirClient fhir, IOracleViewClient oracle, IFhirValidator validator,
            IOperationalLog log = null)
        {
            _fhir = fhir;
            _oracle = oracle;
            _validator = validator;
            _log = log ?? NullOperationalLog.Instance;
        }

        public async Task<IReadOnlyList<IntegrationStatus>> ProbeAllAsync()
        {
            var probes = await Task.WhenAll(ProbeFhirAsync(), ProbeOracleAsync()).ConfigureAwait(false);
            var result = new List<IntegrationStatus>(probes);
            result.Add(ProbeValidator());
            return result;
        }

        private async Task<IntegrationStatus> ProbeFhirAsync()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                await _fhir.GetPatientAsync("__ping__").ConfigureAwait(false);
                return Ok("FHIR Hospital", sw.ElapsedMilliseconds);
            }
            catch (Exception ex) { return Down("FHIR Hospital", ex); }
        }

        private async Task<IntegrationStatus> ProbeOracleAsync()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                await _oracle.PingAsync().ConfigureAwait(false);
                return Ok("Oracle Vistas", sw.ElapsedMilliseconds);
            }
            catch (Exception ex) { return Down("Oracle Vistas", ex); }
        }

        /// <summary>Valida un recurso mínimo: si el validador no carga sus perfiles, falla acá.</summary>
        private IntegrationStatus ProbeValidator()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                _validator.Validate("{\"resourceType\":\"Questionnaire\",\"status\":\"draft\"}");
                return Ok("Validador FHIR", sw.ElapsedMilliseconds);
            }
            catch (Exception ex) { return Down("Validador FHIR", ex); }
        }

        private static IntegrationStatus Ok(string name, long ms) => new IntegrationStatus
        { Name = name, Status = "ok", LastPingUtc = DateTime.UtcNow, LatencyMs = ms };

        private IntegrationStatus Down(string name, Exception ex)
        {
            _log.Warning(ex, "Chequeo de integraciones: " + name + " no responde.");
            return new IntegrationStatus { Name = name, Status = "down", LastPingUtc = DateTime.UtcNow, Detail = "No responde" };
        }
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
