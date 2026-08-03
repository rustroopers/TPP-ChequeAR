namespace ChequeAR.Application.Services
{
    /// <summary>
    /// Un cambio puntual dentro de una sesión de guardado: qué pregunta se tocó y cómo pasó
    /// de un valor a otro. Se serializa y se guarda en <c>AuditEvent.FhirJson</c> para que la
    /// auditoría pueda mostrar "el diff de lo completado en esta sesión" sin tener que
    /// reconstruir estados históricos (la respuesta viva ya no los conserva).
    /// </summary>
    public class ResponseFieldChange
    {
        public string LinkId { get; set; }
        /// <summary>Texto de la pregunta tal como se veía en la versión anclada.</summary>
        public string Text { get; set; }
        /// <summary>Valor antes de esta sesión ("—" si no había respuesta).</summary>
        public string Before { get; set; }
        /// <summary>Valor que quedó guardado en esta sesión.</summary>
        public string After { get; set; }
    }
}
