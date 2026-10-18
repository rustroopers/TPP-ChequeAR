using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Dtos;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Reports;
using ChequeAR.Application.Security;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Application.Services
{
    /// <summary>
    /// Arma la lista de parámetros graficables de un cuestionario y las series de evolución de
    /// un paciente (spec 010). La selección de parámetros sale de la definición VIGENTE del
    /// cuestionario (qué se PUEDE graficar), independiente de si hay datos cargados; las series
    /// salen de las fichas finalizadas del paciente (qué TIENE datos). El spec pide distinguir
    /// explícitamente ambas cosas, por eso viven en dos consultas distintas.
    /// </summary>
    public class PatientTrendService
    {
        private readonly IQuestionnaireRepository _questionnaires;
        private readonly IPatientTrendRepository _trends;
        private readonly ISpecialtyGuard _guard;
        private readonly IClinicalVariableRepository _variables;

        public PatientTrendService(IQuestionnaireRepository questionnaires, IPatientTrendRepository trends,
            ISpecialtyGuard guard, IClinicalVariableRepository variables)
        {
            _questionnaires = questionnaires;
            _trends = trends;
            _guard = guard;
            _variables = variables;
        }

        /// <summary>Lo publicado del cuestionario, con nombres que se distinguen entre sí.</summary>
        private async Task<Questionnaire> StructureAsync(Questionnaire q)
        {
            var structure = PublishedStructure.Union(q);
            await ColumnLabels.ApplyAsync(structure, _variables);
            return structure;
        }

        /// <summary>
        /// Parámetros seleccionables del cuestionario: preguntas numéricas o de fecha, no
        /// repetibles. Devuelve null si el cuestionario no existe (el controller responde 404);
        /// una lista vacía si existe pero no tiene ninguna pregunta graficable.
        /// </summary>
        public async Task<TrendParametersResult> GetSelectableParametersAsync(Guid questionnaireId)
        {
            var q = await _questionnaires.GetAsync(questionnaireId);
            if (q == null) return null;
            // IQuestionnaireRepository no aplica el guard (mismo chequeo que ReportExecutor)
            _guard.EnsureAllows(q.SpecialtyCode);

            var parameters = GraphableQuestions(await StructureAsync(q)).ToList();

            return new TrendParametersResult
            {
                QuestionnaireId = q.Id,
                QuestionnaireTitle = q.Title,
                Parameters = parameters
            };
        }

        /// <summary>
        /// Series de evolución para los parámetros pedidos. Los linkId que no pertenecen al
        /// cuestionario o no son graficables se descartan en silencio (no rompen el resto de la
        /// respuesta). Cada linkId válido aparece siempre en el resultado, con Points vacío si
        /// no tiene datos: el frontend distingue "sin datos" de "error" (FR-011).
        /// </summary>
        public async Task<List<TrendSeriesDto>> GetTrendAsync(
            string patientHospitalId, Guid questionnaireId, IEnumerable<string> linkIds)
        {
            var requested = (linkIds ?? Enumerable.Empty<string>())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (requested.Count == 0) return new List<TrendSeriesDto>();

            var q = await _questionnaires.GetAsync(questionnaireId);
            if (q == null) return new List<TrendSeriesDto>();
            _guard.EnsureAllows(q.SpecialtyCode);

            // Metadata de las preguntas graficables, indexada por linkId. Solo se atienden los
            // linkId pedidos que efectivamente son graficables en este cuestionario.
            var graphable = GraphableQuestions(await StructureAsync(q)).ToDictionary(x => x.LinkId, StringComparer.Ordinal);
            var validLinkIds = requested.Where(graphable.ContainsKey).ToList();
            if (validLinkIds.Count == 0) return new List<TrendSeriesDto>();

            var withData = await _trends.GetSeriesAsync(patientHospitalId, questionnaireId, validLinkIds);
            var pointsByLink = (withData ?? new List<TrendSeriesDto>())
                .Where(s => s.LinkId != null)
                .ToDictionary(s => s.LinkId, s => s.Points ?? new List<TrendPointDto>(), StringComparer.Ordinal);

            // Se arma la serie final desde la metadata (texto/tipo/unidad/rango) y se le enganchan
            // los puntos del repositorio, o una lista vacía si no trajo datos. Se conserva el
            // orden en que se pidieron los parámetros.
            return validLinkIds.Select(linkId =>
            {
                var option = graphable[linkId];
                pointsByLink.TryGetValue(linkId, out var points);
                return new TrendSeriesDto
                {
                    LinkId = option.LinkId,
                    Text = option.Text,
                    Type = option.Type,
                    Unit = option.Unit,
                    NormalRange = option.NormalRange,
                    Points = points ?? new List<TrendPointDto>()
                };
            }).ToList();
        }

        /// <summary>Preguntas del cuestionario que se pueden graficar, ya como opciones de selector.</summary>
        private static IEnumerable<TrendParameterOptionDto> GraphableQuestions(Questionnaire q)
        {
            return (q.Questions ?? new List<Question>())
                .Where(question => GraphableTypeName(question.Type) != null
                                   && !question.RepeatsAllowed
                                   && !string.IsNullOrWhiteSpace(question.LinkId))
                .OrderBy(question => question.OrderIndex)
                .Select(ToOption);
        }

        private static TrendParameterOptionDto ToOption(Question question)
        {
            // JSON de validación corrupto o ausente: se muestra el parámetro igual, sin unidad
            // ni rango — mismo criterio tolerante que usa el resto del sistema.
            var validation = QuestionMetadataReader.ReadValidation(question.ValidationJson);
            var normalRange = (validation != null && (validation.Min.HasValue || validation.Max.HasValue))
                ? new NormalRangeDto { Min = validation.Min, Max = validation.Max }
                : null;

            return new TrendParameterOptionDto
            {
                LinkId = question.LinkId,
                Text = question.Text,
                Type = GraphableTypeName(question.Type),
                Unit = validation?.Unit,
                NormalRange = normalRange
            };
        }

        /// <summary>Nombre en minúscula del tipo si es graficable; null si no lo es.</summary>
        private static string GraphableTypeName(QuestionType type)
        {
            switch (type)
            {
                case QuestionType.Decimal: return "decimal";
                case QuestionType.Integer: return "integer";
                case QuestionType.Date: return "date";
                default: return null;
            }
        }
    }
}
