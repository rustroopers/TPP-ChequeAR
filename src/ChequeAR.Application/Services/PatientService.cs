using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Application.Services
{
    /// Servicio thin que centraliza lookups de pacientes contra el hospital.
    /// ChequeAR no posee la entidad Patient — la consume de la vista Oracle
    /// `VW_PACIENTE` (ver specs/008-mock-hospital-db/contracts/view-schema.md).
    ///
    /// Modo degradado: si Oracle no responde o no devuelve resultados, deriva
    /// los pacientes de las respuestas ya cargadas en ChequeAR
    /// (QuestionnaireResponse + Patient "contained" en el FhirJson). Son datos
    /// reales de la base propia, no de demostración.
    public class PatientService
    {
        private const string PatientView = "VW_PACIENTE";

        /// Columnas del contrato contra las que se busca texto libre.
        private static readonly string[] SearchColumns = { "APELLIDO", "NOMBRE", "DOCUMENTO", "ID_PACIENTE" };

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
                var rows = await _oracle.SearchAsync(PatientView, SearchColumns, query);
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
                var rows = await _oracle.QueryAsync(PatientView,
                    new Dictionary<string, object> { { "ID_PACIENTE", hospitalId } });
                var row = rows.Count > 0 ? rows[0] : null;
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
                    // Caer al código dejaba "reuma" como label en las pantallas de paciente
                    // y encuentro: el editor guarda SpecialtyDisplay en null, así que ese
                    // fallback era el caso normal, no el borde.
                    specialtyLabel = SpecialtyMap.Label(q.SpecialtyCode, q.SpecialtyDisplay);
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

        /// Mapea una fila de VW_PACIENTE. La vista no expone edad ni especialidad:
        /// la edad se calcula de FECHA_NACIMIENTO y la especialidad no pertenece
        /// al paciente sino al episodio (VW_EPISODIO.COD_ESPECIALIDAD).
        private static PatientSearchHit MapRow(IDictionary<string, object> row, string fallbackId)
        {
            var apellido = row.TryGetValue("APELLIDO", out var a) ? a?.ToString() : null;
            var nombre = row.TryGetValue("NOMBRE", out var n) ? n?.ToString() : null;

            string fullName = null;
            if (!string.IsNullOrEmpty(apellido) && !string.IsNullOrEmpty(nombre))
                fullName = apellido + ", " + nombre;
            else
                fullName = apellido ?? nombre;

            return new PatientSearchHit
            {
                HospitalId = row.TryGetValue("ID_PACIENTE", out var id) ? id?.ToString() : fallbackId,
                FullName = fullName,
                AgeLabel = row.TryGetValue("FECHA_NACIMIENTO", out var fn) ? AgeLabelFrom(fn) : null,
                SpecialtyCode = null,
                SpecialtyLabel = null
            };
        }

        private static string AgeLabelFrom(object birthDate)
        {
            if (!(birthDate is DateTime dob)) return null;

            var today = DateTime.Today;
            var years = today.Year - dob.Year;
            if (dob.Date > today.AddYears(-years)) years--;
            if (years >= 1) return years + (years == 1 ? " año" : " años");

            var months = (today.Year - dob.Year) * 12 + today.Month - dob.Month;
            if (today.Day < dob.Day) months--;
            if (months < 0) months = 0;
            return months + (months == 1 ? " mes" : " meses");
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
