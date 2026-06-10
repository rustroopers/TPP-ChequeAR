using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Application.Services
{
    /// Servicio thin que centraliza lookups de pacientes contra el hospital.
    /// ChequeAR no posee la entidad Patient — la consume del FHIR del hospital
    /// y/o de las vistas Oracle (`VW_PACIENTE_DATOS_BASICOS`).
    public class PatientService
    {
        private readonly IHospitalFhirClient _fhir;
        private readonly IOracleViewClient _oracle;
        private readonly IQuestionnaireResponseRepository _qrRepo;

        public PatientService(IHospitalFhirClient fhir, IOracleViewClient oracle, IQuestionnaireResponseRepository qrRepo)
        {
            _fhir = fhir;
            _oracle = oracle;
            _qrRepo = qrRepo;
        }

        public async Task<IReadOnlyList<PatientSearchHit>> SearchAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return new List<PatientSearchHit>();

            // 1) Si parece DNI/HC numérico, intentamos Oracle directamente.
            var filters = new Dictionary<string, object> { { "QUERY", query } };
            var rows = await _oracle.QueryAsync("VW_PACIENTE_DATOS_BASICOS", filters);

            var result = new List<PatientSearchHit>();
            foreach (var row in rows)
            {
                result.Add(new PatientSearchHit
                {
                    HospitalId = row.TryGetValue("HC", out var hc) ? hc?.ToString() : null,
                    FullName = row.TryGetValue("NOMBRE_COMPLETO", out var n) ? n?.ToString() : null,
                    AgeLabel = row.TryGetValue("EDAD_ANIOS", out var e) ? e?.ToString() + " años" : null,
                    SpecialtyCode = row.TryGetValue("ESPECIALIDAD_COD", out var sp) ? sp?.ToString() : null,
                    SpecialtyLabel = row.TryGetValue("ESPECIALIDAD", out var spl) ? spl?.ToString() : null
                });
            }
            return result;
        }

        public async Task<PatientSearchHit> GetAsync(string hospitalId)
        {
            var rows = await _oracle.QueryAsync("VW_PACIENTE_DATOS_BASICOS",
                new Dictionary<string, object> { { "HC", hospitalId } });
            var row = rows.FirstOrDefault();
            if (row == null) return null;
            return new PatientSearchHit
            {
                HospitalId = row.TryGetValue("HC", out var hc) ? hc?.ToString() : hospitalId,
                FullName = row.TryGetValue("NOMBRE_COMPLETO", out var n) ? n?.ToString() : null,
                AgeLabel = row.TryGetValue("EDAD_ANIOS", out var e) ? e?.ToString() + " años" : null,
                SpecialtyCode = row.TryGetValue("ESPECIALIDAD_COD", out var sp) ? sp?.ToString() : null,
                SpecialtyLabel = row.TryGetValue("ESPECIALIDAD", out var spl) ? spl?.ToString() : null
            };
        }

        public async Task<IReadOnlyList<QuestionnaireResponse>> GetEncountersAsync(string hospitalId, int take = 50)
        {
            return await _qrRepo.SearchAsync(hospitalId, null, null, 0, take);
        }
    }

    public class PatientSearchHit
    {
        public string HospitalId { get; set; }
        public string FullName { get; set; }
        public string AgeLabel { get; set; }
        public string SpecialtyCode { get; set; }
        public string SpecialtyLabel { get; set; }
    }
}
