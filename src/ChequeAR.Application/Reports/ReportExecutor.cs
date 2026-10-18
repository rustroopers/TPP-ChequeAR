using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ChequeAR.Application.Dtos;
using ChequeAR.Application.Exceptions;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Security;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Application.Reports
{
    public class ReportExecutor
    {
        /// <summary>
        /// Tope duro, en fichas, de la exportación sincrónica. FR-006 manda a background lo
        /// que pase de 10k; mientras ese camino no exista, es preferible cortar con un error
        /// entendible que tumbar el proceso armando el archivo en memoria.
        /// </summary>
        public const int MaxSyncRows = 10000;

        private readonly IReportQueryRunner _runner;
        private readonly QueryBuilder _builder;
        private readonly IQuestionnaireRepository _questionnaires;
        private readonly ISpecialtyGuard _guard;
        private readonly IClinicalVariableRepository _variables;

        public ReportExecutor(IReportQueryRunner runner, QueryBuilder builder,
            IQuestionnaireRepository questionnaires, ISpecialtyGuard guard, IClinicalVariableRepository variables)
        {
            _runner = runner;
            _builder = builder;
            _questionnaires = questionnaires;
            _guard = guard;
            _variables = variables;
        }

        /// <summary>Lo publicado del cuestionario, con nombres de columna que se distinguen entre sí.</summary>
        private async Task<Questionnaire> StructureAsync(Questionnaire questionnaire)
        {
            var structure = PublishedStructure.Union(questionnaire);
            await ColumnLabels.ApplyAsync(structure, _variables);
            return structure;
        }

        /// <summary>
        /// Una página de la cohorte, ya pivoteada a formato ancho (una fila por ficha).
        /// Devuelve el total REAL de fichas que matchean, no el de la página.
        /// </summary>
        public Task<ReportResult> ExecuteAsync(ReportQuery q) => RunAsync(q, paginate: true);

        /// <summary>La cohorte completa, para exportar. Sin paginar, con tope.</summary>
        public Task<ReportResult> ExecuteAllAsync(ReportQuery q) => RunAsync(q, paginate: false);

        /// <summary>
        /// Estadísticas descriptivas de la cohorte (FR-008). Va sin paginar: una media
        /// calculada sobre la primera página no es la media de la cohorte.
        /// </summary>
        public async Task<ReportStats> ExecuteStatsAsync(ReportQuery q)
        {
            var raw = await RunRawAsync(q, paginate: false);
            var stats = StatsCalculator.Compute(
                new ReportResult { Rows = raw.Rows, Total = raw.Rows.Count },
                raw.Schema,
                raw.TotalResponses);

            // Para que un export "viva" solo: quien lo recibe no tiene la pantalla de
            // reportes al lado para saber qué cuestionario, período o filtros generaron esto.
            stats.QuestionnaireTitle = raw.QuestionnaireTitle;
            stats.DateFrom = q.DateRange?.From;
            stats.DateTo = q.DateRange?.To;
            stats.FiltersSummary = FilterSummary.Describe(q.Filters, raw.Schema);

            return stats;
        }

        private async Task<ReportResult> RunAsync(ReportQuery q, bool paginate)
        {
            var raw = await RunRawAsync(q, paginate);
            var wide = ReportPivot.ToWide(
                new ReportResult { Rows = raw.Rows, Total = raw.Rows.Count }, raw.Schema.Columns, raw.Schema.LabelFor);
            // El total de la cohorte, no el de la página traída.
            wide.Total = raw.TotalResponses;
            return wide;
        }

        /// <summary>Filas en formato largo + el total real de fichas + la metadata usada.</summary>
        /// <summary>
        /// Las preguntas sobre las que se puede filtrar: las de las versiones publicadas, no las
        /// del borrador. Null si el cuestionario no existe.
        /// </summary>
        public async Task<Questionnaire> GetReportStructureAsync(Guid questionnaireId)
        {
            var questionnaire = await _questionnaires.GetAsync(questionnaireId);
            if (questionnaire == null) return null;
            _guard.EnsureAllows(questionnaire.SpecialtyCode);
            return await StructureAsync(questionnaire);
        }

        private async Task<RawResult> RunRawAsync(ReportQuery q, bool paginate)
        {
            // La metadata del cuestionario decide de qué tipo es cada pregunta (y por lo
            // tanto qué columna consultar) y qué columnas tiene el reporte. Es lo que hace
            // que el pivot y el SQL no puedan quedar desalineados: salen de la misma fuente.
            var questionnaire = await _questionnaires.GetAsync(q.QuestionnaireId);
            if (questionnaire == null)
                throw new NotFoundException("El cuestionario del reporte no existe.");
            // Va directo a IQuestionnaireRepository (no a QuestionnaireService.GetAsync), así
            // que el guard de especialidad NO viene aplicado: sin esto, cualquiera que
            // conociera el GUID de un cuestionario podía ejecutar un reporte sobre datos de
            // una especialidad fuera de su scope. Es el mismo chequeo, hecho acá.
            _guard.EnsureAllows(questionnaire.SpecialtyCode);
            var schema = ReportSchema.From(await StructureAsync(questionnaire));

            BuiltQuery built;
            try
            {
                built = _builder.Build(q, schema, paginate);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException
                                       || ex is InvalidCastException || ex is OverflowException)
            {
                // Filtro mal armado (linkId ajeno al cuestionario, operador inválido, valor no
                // convertible): es un error del pedido, con mensaje pensado para quien lo armó.
                throw new ValidationFailedException(new[] { ex.Message });
            }

            // La ejecución la hace el adaptador: quién abre la conexión y cómo se bindean los
            // parámetros no es una decisión del negocio.
            // El tope viaja al adaptador para que no llegue a leer las filas de una cohorte
            // que igual se va a rechazar.
            var ejecutado = await _runner.RunAsync(built, paginate ? (int?)null : MaxSyncRows);

            // El tope de fichas exportables SÍ es una regla de negocio, así que se aplica acá.
            if (!paginate && ejecutado.TotalResponses > MaxSyncRows)
                throw new BusinessRuleException(
                    "La consulta devuelve " + ejecutado.TotalResponses + " fichas y el máximo para exportar en línea es " +
                    MaxSyncRows + ". Acote el rango de fechas o agregue filtros.");

            var rows = ejecutado.Rows;
            var totalResponses = ejecutado.TotalResponses;

            return new RawResult
            {
                Rows = rows, TotalResponses = totalResponses, Schema = schema,
                QuestionnaireTitle = questionnaire.Title
            };
        }

        private sealed class RawResult
        {
            public List<Dictionary<string, object>> Rows { get; set; }
            public int TotalResponses { get; set; }
            public ReportSchema Schema { get; set; }
            public string QuestionnaireTitle { get; set; }
        }

    }


    public class ReportResult
    {
        public int Total { get; set; }
        public IReadOnlyList<Dictionary<string, object>> Rows { get; set; }
    }
}
