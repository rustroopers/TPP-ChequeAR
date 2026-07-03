using System;
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
    ///
    /// Modo degradado: si el hospital no responde (FHIR/Oracle caídos) o no
    /// devuelve resultados, deriva los pacientes de las respuestas ya cargadas
    /// en ChequeAR (QuestionnaireResponse + Patient "contained" en el FhirJson).
    /// Esto permite operar el sistema con datos propios / de seed.
    public class PatientService
    {
        private readonly IHospitalFhirClient _fhir;
        private readonly IOracleViewClient _oracle;
        private readonly IQuestionnaireResponseRepository _qrRepo;
        private readonly IQuestionnaireRepository _qRepo;

        public PatientService(IHospitalFhirClient fhir, IOracleViewClient oracle, IQuestionnaireResponseRepository qrRepo, IQuestionnaireRepository qRepo)
        {
            _fhir = fhir;
            _oracle = oracle;
            _qrRepo = qrRepo;
            _qRepo = qRepo;
        }

        public async Task<IReadOnlyList<PatientSearchHit>> SearchAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return new List<PatientSearchHit>();

            try
            {
                var filters = new Dictionary<string, object> { { "QUERY", query } };
                var rows = await _oracle.QueryAsync("VW_PACIENTE_DATOS_BASICOS", filters);
                var result = new List<PatientSearchHit>();
                foreach (var row in rows) result.Add(MapRow(row, null));
                if (result.Count > 0) return result;
            }
            catch { /* hospital no disponible: caemos a datos propios */ }

            return await SearchFromResponsesAsync(query);
        }

        public async Task<PatientSearchHit> GetAsync(string hospitalId)
        {
            try
            {
                var rows = await _oracle.QueryAsync("VW_PACIENTE_DATOS_BASICOS",
                    new Dictionary<string, object> { { "HC", hospitalId } });
                var row = rows.FirstOrDefault();
                if (row != null) return MapRow(row, hospitalId);
            }
            catch { /* hospital no disponible */ }

            var responses = await _qrRepo.SearchAsync(hospitalId, null, null, 0, 50);
            if (responses.Count == 0) return null;
            return await BuildHitAsync(hospitalId, responses);
        }

        public async Task<IReadOnlyList<QuestionnaireResponse>> GetEncountersAsync(string hospitalId, int take = 50)
        {
            return await _qrRepo.SearchAsync(hospitalId, null, null, 0, take);
        }

        // ---- Modo degradado: pacientes derivados de las respuestas propias ----

        private async Task<IReadOnlyList<PatientSearchHit>> SearchFromResponsesAsync(string query)
        {
            var responses = await _qrRepo.SearchAsync(null, null, null, 0, 500);
            var hits = new List<PatientSearchHit>();
            foreach (var g in responses.Where(r => !string.IsNullOrEmpty(r.PatientHospitalId))
                                       .GroupBy(r => r.PatientHospitalId))
            {
                hits.Add(await BuildHitAsync(g.Key, g.ToList()));
            }

            var q = query.Trim().ToLowerInvariant();
            return hits.Where(h =>
                (h.HospitalId ?? "").ToLowerInvariant().Contains(q) ||
                (h.FullName ?? "").ToLowerInvariant().Contains(q)).ToList();
        }

        private async Task<PatientSearchHit> BuildHitAsync(string hospitalId, IReadOnlyList<QuestionnaireResponse> responses)
        {
            var latest = responses.OrderByDescending(r => r.AuthoredAt).First();
            var demo = FhirPatientReader.Read(latest.FhirJson);

            string specialtyCode = null, specialtyLabel = null;
            try
            {
                var q = await _qRepo.GetAsync(latest.QuestionnaireId);
                if (q != null)
                {
                    specialtyCode = q.SpecialtyCode;
                    specialtyLabel = q.SpecialtyDisplay ?? q.SpecialtyCode;
                }
            }
            catch { /* ignore */ }

            return new PatientSearchHit
            {
                HospitalId = hospitalId,
                FullName = demo?.Name ?? hospitalId,
                AgeLabel = demo?.AgeLabel,
                SpecialtyCode = specialtyCode ?? "general",
                SpecialtyLabel = specialtyLabel ?? "General"
            };
        }

        private static PatientSearchHit MapRow(IDictionary<string, object> row, string fallbackHc)
        {
            return new PatientSearchHit
            {
                HospitalId = row.TryGetValue("HC", out var hc) ? hc?.ToString() : fallbackHc,
                FullName = row.TryGetValue("NOMBRE_COMPLETO", out var n) ? n?.ToString() : null,
                AgeLabel = row.TryGetValue("EDAD_ANIOS", out var e) ? e?.ToString() + " años" : null,
                SpecialtyCode = row.TryGetValue("ESPECIALIDAD_COD", out var sp) ? sp?.ToString() : null,
                SpecialtyLabel = row.TryGetValue("ESPECIALIDAD", out var spl) ? spl?.ToString() : null
            };
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
