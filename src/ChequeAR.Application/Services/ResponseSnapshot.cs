using System.Collections.Generic;

namespace ChequeAR.Application.Services
{
    /// <summary>
    /// Foto completa de una ficha al finalizarla: todas las secciones con todas sus preguntas
    /// y la respuesta final (o "—" si quedó sin responder). Se serializa en el
    /// <c>AuditEvent.FhirJson</c> del evento de finalización para que la auditoría muestre el
    /// cuestionario completo tal como quedó, sin depender de la ficha viva (que podría
    /// borrarse). Es distinto del diff de "Avance guardado", que sólo muestra lo que cambió en
    /// una sesión.
    /// </summary>
    public class ResponseSnapshotSection
    {
        public string Title { get; set; }
        public List<ResponseSnapshotAnswer> Answers { get; set; } = new List<ResponseSnapshotAnswer>();
    }

    public class ResponseSnapshotAnswer
    {
        public string Text { get; set; }
        public string Answer { get; set; }
    }
}
