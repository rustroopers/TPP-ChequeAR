using System;

namespace ChequeAR.Application.Services
{
    /// <summary>
    /// Foto de una consulta guardada al momento de crearla, modificarla o eliminarla,
    /// serializada en <c>AuditEvent.FhirJson</c>. Guardar la foto —en vez de resolver el
    /// <see cref="Domain.Entities.SavedReport"/> en vivo al abrir el detalle— mantiene el
    /// registro inmutable: si la consulta se edita o se borra después, la auditoría sigue
    /// mostrando cómo era en el instante del evento.
    /// </summary>
    public class SavedReportAuditSnapshot
    {
        public string Name { get; set; }
        public string Scope { get; set; }          // private | specialty
        public string SpecialtyCode { get; set; }
        public Guid QuestionnaireId { get; set; }
        /// <summary>La definición de la consulta (filtros/fechas/etc.) tal como la mandó el cliente.</summary>
        public string QueryJson { get; set; }
    }
}
