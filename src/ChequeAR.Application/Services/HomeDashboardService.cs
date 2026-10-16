using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Security;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Application.Services
{
    /// <summary>Una ficha reciente, ya resuelta contra su cuestionario.</summary>
    public class RecentEncounterDto
    {
        public Guid Id { get; set; }
        public string PatientName { get; set; }
        public string PatientHospitalId { get; set; }
        public Guid QuestionnaireId { get; set; }
        public string QuestionnaireTitle { get; set; }
        public string SpecialtyCode { get; set; }
        public string SpecialtyDisplay { get; set; }
        public ResponseStatus Status { get; set; }
        public DateTime LastSavedAt { get; set; }
    }

    public class DashboardKpis
    {
        public int Questionnaires { get; set; }
        public int Encounters { get; set; }
        public int Users { get; set; }
        public int Specialties { get; set; }
    }

    /// <summary>
    /// Arma el tablero de inicio.
    ///
    /// Esto vivía en el controller, con un N+1 contra el repositorio de cuestionarios y un
    /// diccionario de cache armado a mano en medio del código de presentación. Acá, además
    /// de estar en la capa correcta, los títulos se resuelven en UNA consulta.
    /// </summary>
    public class HomeDashboardService
    {
        private readonly IQuestionnaireResponseRepository _responses;
        private readonly IQuestionnaireRepository _questionnaires;
        private readonly IUserRepository _users;
        private readonly SpecialtyCatalogService _specialties;
        private readonly ISpecialtyGuard _guard;

        public HomeDashboardService(
            IQuestionnaireResponseRepository responses,
            IQuestionnaireRepository questionnaires,
            IUserRepository users,
            SpecialtyCatalogService specialties,
            ISpecialtyGuard guard)
        {
            _responses = responses;
            _questionnaires = questionnaires;
            _users = users;
            _specialties = specialties;
            _guard = guard;
        }

        public async Task<IReadOnlyList<RecentEncounterDto>> RecentEncountersAsync(int take = 5)
        {
            // Recientes va restringido por especialidad: un Jefe/Medico ve SOLO fichas de las
            // suyas. El scope lo resuelve el guard (Administrador => sin restriccion), y el
            // filtrado ocurre en la consulta, no despues de traer las filas -- si se filtrara
            // aca, "las ultimas 5" podrian quedar en menos de 5 al descartar las ajenas.
            var scope = _guard.Scope;
            var recientes = await _responses.SearchAsync(null, null, null, 0, take, scope);
            if (recientes.Count == 0) return new List<RecentEncounterDto>();

            // Una sola consulta, sólo con la cabecera de cada cuestionario. Va directo al
            // repositorio: el scope ya se aplicó al buscar las fichas.
            var porId = (await _questionnaires.GetHeadersAsync(recientes.Select(r => r.QuestionnaireId)))
                .ToDictionary(q => q.Id);

            var cat = await _specialties.GetAsync();
            return recientes.Select(r =>
            {
                porId.TryGetValue(r.QuestionnaireId, out var q);
                return new RecentEncounterDto
                {
                    Id = r.Id,
                    // El nombre sale del Patient embebido en el recurso; si no está, la
                    // historia clínica. Nunca se inventa un nombre.
                    PatientName = FhirPatientReader.ReadName(r.FhirJson) ?? r.PatientHospitalId,
                    PatientHospitalId = r.PatientHospitalId,
                    QuestionnaireId = r.QuestionnaireId,
                    QuestionnaireTitle = q?.Title ?? r.QuestionnaireId.ToString(),
                    SpecialtyCode = cat.CodeOrDefault(q?.SpecialtyCode),
                    SpecialtyDisplay = q?.SpecialtyDisplay,
                    Status = r.Status,
                    LastSavedAt = r.LastSavedAt
                };
            }).ToList();
        }

        public async Task<DashboardKpis> KpisAsync()
        {
            var scope = _guard.Scope;
            var especialidades = await _questionnaires.GetSpecialtyCodesAsync();
            if (scope.IsUnrestricted)
            {
                return new DashboardKpis
                {
                    Questionnaires = await _questionnaires.CountAsync(null, null, null),
                    Users = await _users.CountAsync(null, null, null, true),
                    // COUNT en la base: antes se traían hasta 1000 fichas completas para contarlas,
                    // y pasado ese número el indicador quedaba clavado en 1000.
                    Encounters = await _responses.CountAsync(),
                    Specialties = especialidades.Count
                };
            }

            // Un usuario puede tener varias especialidades: se cuentan ids distintos.
            var usuarios = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var codigo in scope.Codes)
                foreach (var u in await _users.ListAsync(null, null, codigo, true, 0, int.MaxValue))
                    usuarios.Add(u.Id);

            return new DashboardKpis
            {
                Questionnaires = scope.IsEmpty ? 0 : await _questionnaires.CountAsync(null, null, null, scope.Codes),
                Users = usuarios.Count,
                Encounters = scope.IsEmpty ? 0 : await _responses.CountAsync(scope: scope),
                Specialties = especialidades.Count(scope.Allows)
            };
        }
    }
}
