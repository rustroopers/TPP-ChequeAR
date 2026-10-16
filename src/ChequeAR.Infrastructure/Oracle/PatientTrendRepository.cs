using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ChequeAR.Application.Dtos;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Reports;
using Oracle.ManagedDataAccess.Client;

namespace ChequeAR.Infrastructure.Oracle
{
    /// <summary>
    /// Lee los puntos de evolución de un paciente desde la vista analítica `vw_FlatResponses`.
    ///
    /// Se usa la MISMA vista que el módulo de Reportes (formato largo: una fila por
    /// ficha+pregunta) porque ya expone las columnas que hacen falta —CompletedAt, LinkId y las
    /// columnas de valor— sin tocar el esquema. Un detalle clave: la vista filtra
    /// `WHERE qr."Status" IN (1, 2)` en su propia definición, así que "solo fichas finalizadas"
    /// (FR-007) ya viene garantizado por la fuente, no hace falta repetir el filtro acá.
    ///
    /// A diferencia de los reportes, el filtro de esta consulta es fijo (paciente + cuestionario
    /// + lista de linkIds), así que NO se reutiliza la DSL de QueryBuilder/FilterNode: una
    /// consulta dedicada y directa es más simple y no arrastra la maquinaria de cohortes.
    ///
    /// Todo lo específico del driver Oracle vive en este archivo, igual que en
    /// <see cref="OracleReportQueryRunner"/>.
    /// </summary>
    public class PatientTrendRepository : IPatientTrendRepository
    {
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private readonly string _connectionString;

        public PatientTrendRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<List<TrendSeriesDto>> GetSeriesAsync(
            string patientHospitalId, Guid questionnaireId, IEnumerable<string> linkIds)
        {
            var ids = (linkIds ?? Enumerable.Empty<string>())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Distinct()
                .ToList();

            // Sin paciente o sin linkIds no hay nada que consultar: el servicio ya validó los
            // casos de borde, pero una consulta con IN () vacío sería SQL inválido.
            if (string.IsNullOrWhiteSpace(patientHospitalId) || ids.Count == 0)
                return new List<TrendSeriesDto>();

            // Una serie por linkId; se completan sus puntos a medida que llegan las filas.
            var seriesByLink = ids.ToDictionary(
                l => l,
                l => new TrendSeriesDto { LinkId = l, Points = new List<TrendPointDto>() },
                StringComparer.Ordinal);

            var sql = new StringBuilder();
            // Fecha de finalización (FR-008); AuthoredAt sólo por si faltara.
            sql.AppendLine("SELECT v.\"ResponseId\", COALESCE(v.\"CompletedAt\", v.\"AuthoredAt\") AS \"CompletedAt\", v.\"LinkId\",");
            sql.AppendLine("       v.\"ValueDecimal\", v.\"ValueInteger\", v.\"ValueDate\"");
            sql.AppendLine("FROM \"vw_FlatResponses\" v");
            sql.AppendLine("WHERE v.\"PatientHospitalId\" = :PatientHospitalId");
            sql.AppendLine("  AND v.\"QuestionnaireId\" = :QuestionnaireId");
            sql.Append("  AND v.\"LinkId\" IN (");
            for (int i = 0; i < ids.Count; i++)
            {
                if (i > 0) sql.Append(", ");
                sql.Append(":l").Append(i);
            }
            sql.AppendLine(")");
            // Orden estable y cronológico por serie: FR-008 exige los puntos en orden de fecha.
            sql.AppendLine("ORDER BY v.\"LinkId\", 2");

            using (var conn = new OracleConnection(_connectionString))
            {
                await conn.OpenAsync();
                using (var cmd = new OracleCommand(sql.ToString(), conn))
                {
                    cmd.BindByName = true;
                    cmd.CommandTimeout = EF.ChequeARDbContext.CommandTimeoutSeconds;
                    cmd.Parameters.Add(new OracleParameter("PatientHospitalId", OracleDbType.Varchar2, patientHospitalId, ParameterDirection.Input));
                    // El identificador se guarda como RAW(16): el driver no sabe bindear un Guid
                    // directamente (ver OracleReportQueryRunner), hay que pasarle los 16 bytes.
                    cmd.Parameters.Add(new OracleParameter("QuestionnaireId", OracleDbType.Raw, 16, questionnaireId.ToByteArray(), ParameterDirection.Input));
                    for (int i = 0; i < ids.Count; i++)
                        cmd.Parameters.Add(new OracleParameter("l" + i, OracleDbType.Varchar2, ids[i], ParameterDirection.Input));

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        var oResponseId = reader.GetOrdinal("ResponseId");
                        var oCompletedAt = reader.GetOrdinal("CompletedAt");
                        var oLinkId = reader.GetOrdinal("LinkId");
                        var oDecimal = reader.GetOrdinal("ValueDecimal");
                        var oInteger = reader.GetOrdinal("ValueInteger");
                        var oDate = reader.GetOrdinal("ValueDate");

                        while (await reader.ReadAsync())
                        {
                            var linkId = reader.GetString(oLinkId);
                            if (!seriesByLink.TryGetValue(linkId, out var serie)) continue;

                            var value = ReadValue(reader, oDecimal, oInteger, oDate);
                            if (value == null) continue;   // ítem sin valor cargado: no aporta punto

                            serie.Points.Add(new TrendPointDto
                            {
                                ResponseId = ToGuid(reader.GetValue(oResponseId)),
                                Date = ArgentinaClock.FromUtc(reader.GetDateTime(oCompletedAt)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                                Value = value.Value
                            });
                        }
                    }
                }
            }

            // Solo las series con datos: el servicio completa con series vacías los linkIds que
            // no devolvieron nada, ya con la metadata del cuestionario.
            return seriesByLink.Values.Where(s => s.Points.Count > 0).ToList();
        }

        /// <summary>
        /// Valor del punto según qué columna de la vista trae dato. Un ítem de una pregunta
        /// numérica/fecha llena exactamente una de las tres; las de tipo Date se transportan
        /// como epoch en milisegundos para que viajen como número (el frontend las re-formatea).
        /// </summary>
        private static decimal? ReadValue(DbDataReader reader, int oDecimal, int oInteger, int oDate)
        {
            if (!reader.IsDBNull(oDecimal)) return reader.GetDecimal(oDecimal);
            if (!reader.IsDBNull(oInteger)) return reader.GetDecimal(oInteger);
            if (!reader.IsDBNull(oDate))
            {
                // La fecha del calendario, sin hora: el frontend la formatea en UTC.
                var d = DateTime.SpecifyKind(reader.GetDateTime(oDate).Date, DateTimeKind.Utc);
                return (decimal)(d - Epoch).TotalMilliseconds;
            }
            return null;
        }

        /// <summary>El identificador vuelve como RAW(16) => byte[]; se reusa la normalización del runner de reportes.</summary>
        private static Guid ToGuid(object value)
        {
            var normalized = OracleReportQueryRunner.Normalizar(value);
            return normalized is Guid g ? g : Guid.Empty;
        }
    }
}
