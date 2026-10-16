using System;
using System.Collections.Generic;
using System.Linq;
using ChequeAR.Application.Dtos;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using Newtonsoft.Json;

namespace ChequeAR.Application.Services
{
    /// <summary>
    /// Registra los accesos de LECTURA a datos clínicos: exportar un reporte o sus
    /// estadísticas, ver la evolución de un paciente y exportar la propia auditoría. Las
    /// altas y cambios los auditan los servicios que los hacen; esto cubre lo que no modifica
    /// nada y por eso no dejaba rastro.
    ///
    /// Ver un reporte en pantalla NO se registra: sólo cuando los datos salen del sistema.
    /// Excepción: la lectura de una ficha (<see cref="ResponseRead"/>).
    ///
    /// Cada exportación lleva la consulta completa (FR-009: QUÉ se exportó, no sólo que hubo
    /// una exportación) en el mismo formato que la foto de un reporte guardado, para que el
    /// detalle de auditoría la muestre igual.
    /// </summary>
    public class AccessAuditService
    {
        private const string CategoryReport = "report";
        private const string CategoryExport = "export";

        private readonly AuditDispatcher _audit;
        private readonly ICurrentUser _user;

        public AccessAuditService(AuditDispatcher audit, ICurrentUser user)
        {
            _audit = audit;
            _user = user;
        }

        public void ReportExported(ReportQuery query, int total)
        {
            if (query == null) return;
            Export("Report/export?questionnaire=" + query.QuestionnaireId + "&rows=" + total, query);
        }

        public void StatsExported(ReportQuery query, int questions)
        {
            if (query == null) return;
            Export("Report/statsExport?questionnaire=" + query.QuestionnaireId + "&questions=" + questions, query);
        }

        /// <summary>Evolución de un paciente: queda a qué paciente se miró y qué parámetros.</summary>
        public void TrendViewed(string patientHospitalId, Guid questionnaireId, IEnumerable<string> linkIds)
        {
            var evt = AuditEventBuilder.For("read", "Patient/" + patientHospitalId + "$trend", _user.Id, category: CategoryReport);
            evt.FhirJson = JsonConvert.SerializeObject(new TrendAuditSnapshot
            {
                PatientHospitalId = patientHospitalId,
                QuestionnaireId = questionnaireId,
                LinkIds = (linkIds ?? Enumerable.Empty<string>()).ToList()
            });
            _audit.Enqueue(evt);
        }

        /// <summary>
        /// Se persiste en el acto, no por la cola. No cuenta la relectura del borrador por quien
        /// lo está editando (lock o autor).
        /// </summary>
        public Task ResponseRead(QuestionnaireResponse response)
        {
            if (response == null) return Task.CompletedTask;
            var editing = response.Status == ResponseStatus.InProgress
                          && (string.Equals(response.LockedByUserId, _user.Id, StringComparison.OrdinalIgnoreCase)
                              || string.Equals(response.AuthorUserId, _user.Id, StringComparison.OrdinalIgnoreCase));
            if (editing) return Task.CompletedTask;

            var evt = AuditEventBuilder.For("read", "QuestionnaireResponse/" + response.Id, _user.Id);
            return _audit.PersistImmediatelyAsync(evt);
        }

        /// <summary>
        /// Búsqueda de pacientes. El término (nombre o DNI) no se guarda: va su SHA-256, que
        /// alcanza para comparar dos búsquedas iguales.
        /// </summary>
        public Task PatientsSearched(string term, int results)
        {
            if (string.IsNullOrWhiteSpace(term)) return Task.CompletedTask;
            var evt = AuditEventBuilder.For("read", "Patient$search", _user.Id);
            evt.FhirJson = JsonConvert.SerializeObject(new PatientSearchAuditSnapshot { TermSha256 = Sha256(term.Trim().ToLowerInvariant()), Results = results });
            return _audit.PersistImmediatelyAsync(evt);
        }

        public Task PatientRead(string patientHospitalId)
            => _audit.PersistImmediatelyAsync(AuditEventBuilder.For("read", "Patient/" + patientHospitalId, _user.Id));

        public Task PatientHistoryViewed(string patientHospitalId)
            => _audit.PersistImmediatelyAsync(AuditEventBuilder.For("read", "Patient/" + patientHospitalId + "$history", _user.Id));

        public Task AttachmentUploaded(Guid attachmentId)
            => _audit.PersistImmediatelyAsync(AuditEventBuilder.For("create", "Attachment/" + attachmentId, _user.Id));

        public Task AttachmentDownloaded(Guid attachmentId)
            => _audit.PersistImmediatelyAsync(AuditEventBuilder.For("read", "Attachment/" + attachmentId, _user.Id));

        private static string Sha256(string value)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>Exportación del propio registro de auditoría, con los filtros aplicados.</summary>
        public void AuditExported(AuditExportSnapshot filters, int rows)
        {
            var evt = AuditEventBuilder.For("execute", "AuditEvent/export?rows=" + rows, _user.Id, category: CategoryExport);
            evt.FhirJson = JsonConvert.SerializeObject(filters ?? new AuditExportSnapshot());
            _audit.Enqueue(evt);
        }

        private void Export(string entityRef, ReportQuery query)
        {
            var evt = AuditEventBuilder.For("execute", entityRef, _user.Id, category: CategoryExport);
            evt.FhirJson = JsonConvert.SerializeObject(new SavedReportAuditSnapshot
            {
                QuestionnaireId = query.QuestionnaireId,
                QueryJson = JsonConvert.SerializeObject(query, SinNulos)
            });
            _audit.Enqueue(evt);
        }

        private static readonly JsonSerializerSettings SinNulos =
            new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
    }

    /// <summary>Foto de una consulta de evolución, serializada en el evento.</summary>
    public class TrendAuditSnapshot
    {
        public string PatientHospitalId { get; set; }
        public Guid QuestionnaireId { get; set; }
        public List<string> LinkIds { get; set; } = new List<string>();
    }

    public class PatientSearchAuditSnapshot
    {
        public string TermSha256 { get; set; }
        public int Results { get; set; }
    }

    /// <summary>Filtros con los que se exportó la auditoría, tal como estaban en pantalla.</summary>
    public class AuditExportSnapshot
    {
        public string User { get; set; }
        public string Action { get; set; }
        public string Questionnaire { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public string Format { get; set; }
    }
}
