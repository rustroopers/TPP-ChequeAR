using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Fhir;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Security;
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
    ///
    /// Las fichas se filtran por las especialidades de quien consulta (ISpecialtyGuard).
    public class PatientService : IPatientDemographicsSource
    {
        private const string PatientView = "VW_PACIENTE";

        /// Columnas del contrato contra las que se busca texto libre.
        private static readonly string[] SearchColumns = { "APELLIDO", "NOMBRE", "DOCUMENTO", "ID_PACIENTE" };

        private readonly IHospitalFhirClient _fhir;
        private readonly IOracleViewClient _oracle;
        private readonly IQuestionnaireResponseRepository _qrRepo;
        private readonly IQuestionnaireRepository _qRepo;
        private readonly SpecialtyCatalogService _specialties;
        private readonly ISpecialtyGuard _guard;

        public PatientService(IHospitalFhirClient fhir, IOracleViewClient oracle, IQuestionnaireResponseRepository qrRepo, IQuestionnaireRepository qRepo, SpecialtyCatalogService specialties, ISpecialtyGuard guard)
        {
            _guard = guard;
            _fhir = fhir;
            _oracle = oracle;
            _qrRepo = qrRepo;
            _qRepo = qRepo;
            _specialties = specialties;
        }

        /// <summary>
        /// Tope de resultados de la búsqueda de pacientes. Llegar a este número significa que
        /// puede haber más coincidencias: el total no se conoce.
        /// </summary>
        public const int MaxSearchResults = 50;

        /// <summary>Lo más que se trae para paginar una búsqueda (FHIR _offset + _count).</summary>
        public const int MaxSearchWindow = 1000;

        // con una sola letra coincide cualquier paciente
        public const int MinSearchLength = 2;

        public async Task<IReadOnlyList<PatientSearchHit>> SearchAsync(string query, int limit = MaxSearchResults)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < MinSearchLength)
                return new List<PatientSearchHit>();
            limit = Math.Max(1, Math.Min(limit, MaxSearchWindow));

            try
            {
                var rows = await _oracle.SearchAsync(PatientView, SearchColumns, query, limit);
                var result = new List<PatientSearchHit>();
                foreach (var row in rows) result.Add(MapRow(row, null));
                if (result.Count > 0) return result;
            }
            catch { /* hospital no disponible: caemos a datos propios */ }

            return await SearchFromResponsesAsync(query, limit);
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

            var responses = await _qrRepo.SearchAsync(hospitalId, null, null, 0, 50, _guard.Scope);
            if (responses.Count == 0) return null;
            return await BuildHitAsync(hospitalId, responses);
        }

        /// <summary>
        /// Sólo el hospital, sin caer a las fichas propias: ahí el nombre puede ser la HC, y
        /// embeberlo como paciente sería inventarlo.
        /// </summary>
        public async Task<PatientDemographicsDto> TryGetAsync(string hospitalId)
        {
            if (string.IsNullOrWhiteSpace(hospitalId)) return null;
            try
            {
                var rows = await _oracle.QueryAsync(PatientView,
                    new Dictionary<string, object> { { "ID_PACIENTE", hospitalId } });
                if (rows.Count == 0) return null;
                var row = rows[0];
                var hit = MapRow(row, hospitalId);
                if (string.IsNullOrWhiteSpace(hit.FullName)) return null;
                return new PatientDemographicsDto
                {
                    FullName = hit.FullName,
                    Gender = row.TryGetValue("SEXO", out var sexo) ? Gender(sexo?.ToString()) : null,
                    BirthDate = row.TryGetValue("FECHA_NACIMIENTO", out var fn) && fn is DateTime dob ? dob : (DateTime?)null
                };
            }
            catch { return null; /* hospital no disponible */ }
        }

        private static string Gender(string sexo)
        {
            switch ((sexo ?? "").Trim().ToUpperInvariant())
            {
                case "M": return "male";
                case "F": return "female";
                case "O": return "other";
                default: return null;
            }
        }

        public async Task<IReadOnlyList<QuestionnaireResponse>> GetEncountersAsync(string hospitalId, int take = 50, int skip = 0)
        {
            return await _qrRepo.SearchAsync(hospitalId, null, null, skip, take, _guard.Scope);
        }

        /// <summary>Total de fichas del paciente visibles para quien consulta, sin el tope de <see cref="GetEncountersAsync"/>.</summary>
        public Task<int> CountEncountersAsync(string hospitalId) => _qrRepo.CountAsync(hospitalId, scope: _guard.Scope);

        // ---- Modo degradado: pacientes derivados de las respuestas propias ----

        /// <summary>Fichas recientes entre las que se busca cuando el hospital no responde.</summary>
        private const int DegradedScan = 500;

        private async Task<IReadOnlyList<PatientSearchHit>> SearchFromResponsesAsync(string query, int limit)
        {
            var responses = await _qrRepo.SearchAsync(null, null, null, 0, DegradedScan, _guard.Scope);
            var q = query.Trim().ToLowerInvariant();

            // Se filtra ANTES de armar cada resultado: el nombre sale del recurso FHIR que ya
            // está en memoria, así que matchear es barato; lo caro (resolver la especialidad)
            // se hace sólo para los pacientes que coinciden.
            var matches = responses
                .Where(r => !string.IsNullOrEmpty(r.PatientHospitalId))
                .GroupBy(r => r.PatientHospitalId)
                .Select(g => new { HospitalId = g.Key, Responses = (IReadOnlyList<QuestionnaireResponse>)g.ToList() })
                .Where(p => p.HospitalId.ToLowerInvariant().Contains(q)
                            || (LatestDemographics(p.Responses)?.Name ?? "").ToLowerInvariant().Contains(q))
                .ToList();

            var catalog = await _specialties.GetAsync();
            var specialtyByQuestionnaire = new Dictionary<Guid, string>();
            var hits = new List<PatientSearchHit>();
            foreach (var m in matches.Take(limit))
                hits.Add(await BuildHitAsync(m.HospitalId, m.Responses, catalog, specialtyByQuestionnaire));
            return hits;
        }

        private async Task<PatientSearchHit> BuildHitAsync(string hospitalId, IReadOnlyList<QuestionnaireResponse> responses)
            => await BuildHitAsync(hospitalId, responses, await _specialties.GetAsync(), new Dictionary<Guid, string>());

        /// <param name="specialtyByQuestionnaire">Caché por cuestionario: varios pacientes suelen compartirlo.</param>
        private async Task<PatientSearchHit> BuildHitAsync(
            string hospitalId, IReadOnlyList<QuestionnaireResponse> responses,
            SpecialtyCatalog catalog, Dictionary<Guid, string> specialtyByQuestionnaire)
        {
            var latest = responses.OrderByDescending(r => r.AuthoredAt).First();
            var demo = FhirPatientReader.Read(latest.FhirJson);

            // Sólo el código de especialidad (una columna), no el agregado del cuestionario.
            if (!specialtyByQuestionnaire.TryGetValue(latest.QuestionnaireId, out var specialtyCode))
            {
                try { specialtyCode = await _qRepo.GetSpecialtyCodeAsync(latest.QuestionnaireId); }
                catch { specialtyCode = null; /* modo degradado: sin especialidad, se usa la de omisión */ }
                specialtyByQuestionnaire[latest.QuestionnaireId] = specialtyCode;
            }

            return new PatientSearchHit
            {
                HospitalId = hospitalId,
                FullName = demo?.Name ?? hospitalId,
                AgeLabel = demo?.AgeLabel,
                BirthDateLabel = demo?.BirthDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                SpecialtyCode = catalog.CodeOrDefault(specialtyCode),
                SpecialtyLabel = catalog.Label(specialtyCode)
            };
        }

        private static PatientDemographics LatestDemographics(IReadOnlyList<QuestionnaireResponse> responses)
            => FhirPatientReader.Read(responses.OrderByDescending(r => r.AuthoredAt).First().FhirJson);

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
                Document = row.TryGetValue("DOCUMENTO", out var doc) ? doc?.ToString() : null,
                AgeLabel = row.TryGetValue("FECHA_NACIMIENTO", out var fn) ? AgeLabelFrom(fn) : null,
                BirthDateLabel = row.TryGetValue("FECHA_NACIMIENTO", out var fn2) && fn2 is DateTime dob ? dob.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : null,
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
        public string Document { get; set; }
        public string AgeLabel { get; set; }
        public string BirthDateLabel { get; set; }
        public string SpecialtyCode { get; set; }
        public string SpecialtyLabel { get; set; }
    }
}
