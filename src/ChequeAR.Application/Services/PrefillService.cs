using System.Collections.Generic;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;

namespace ChequeAR.Application.Services
{
    public class PrefillService
    {
        private readonly IHospitalFhirClient _fhir;
        private readonly IOracleViewClient _oracle;

        public PrefillService(IHospitalFhirClient fhir, IOracleViewClient oracle)
        {
            _fhir = fhir;
            _oracle = oracle;
        }

        public async Task<IReadOnlyDictionary<string, object>> ResolveAsync(string patientId, IReadOnlyList<PrefillRequest> requests)
        {
            var result = new Dictionary<string, object>();
            foreach (var req in requests)
            {
                if (req.Source == "fhir-observation")
                {
                    result[req.LinkId] = await _fhir.GetLastObservationAsync(patientId, req.Code);
                }
                else if (req.Source == "oracle-view")
                {
                    var rows = await _oracle.QueryAsync(req.ViewName, new Dictionary<string, object> { { "ID_PACIENTE", patientId } });
                    result[req.LinkId] = rows;
                }
            }
            return result;
        }
    }

    public class PrefillRequest
    {
        public string LinkId { get; set; }
        public string Source { get; set; }      // fhir-observation | oracle-view
        public string Code { get; set; }
        public string ViewName { get; set; }
    }
}
