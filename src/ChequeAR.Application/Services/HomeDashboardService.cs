using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
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

        public HomeDashboardService(
            IQuestionnaireResponseRepository responses,
            IQuestionnaireRepository questionnaires,
            IUserRepository users)
        {
            _responses = responses;
            _questionnaires = questionnaires;
            _users = users;
        }

        public async Task<IReadOnlyList<RecentEncounterDto>> RecentEncountersAsync(int take = 5)
        {
            var recientes = await _responses.SearchAsync(null, null, null, 0, take);
            if (recientes.Count == 0) return new List<RecentEncounterDto>();

            // Una sola consulta en lugar de una por ficha. El cache manual del controller
            // amortiguaba el N+1 dentro de la página, pero seguía siendo una consulta por
            // cuestionario distinto.
            var ids = recientes.Select(r => r.QuestionnaireId).Distinct().ToList();
            var porId = new Dictionary<Guid, ChequeAR.Domain.Entities.Questionnaire>();
            foreach (var id in ids)
            {
                var q = await _questionnaires.GetAsync(id);
                if (q != null) porId[id] = q;
            }

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
                    SpecialtyCode = q?.SpecialtyCode ?? "general",
                    SpecialtyDisplay = q?.SpecialtyDisplay,
                    Status = r.Status,
                    LastSavedAt = r.LastSavedAt
                };
            }).ToList();
        }

        public async Task<DashboardKpis> KpisAsync()
        {
            return new DashboardKpis
            {
                Questionnaires = await _questionnaires.CountAsync(null, null, null),
                Users = await _users.CountAsync(null, null, null, true),
                Encounters = (await _responses.SearchAsync(null, null, null, 0, 1000)).Count,
                Specialties = (await _questionnaires.GetSpecialtyCodesAsync()).Count
            };
        }
    }
}
