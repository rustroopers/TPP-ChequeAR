using System;

namespace ChequeAR.Domain.Entities
{
    public class AuditEvent
    {
        public Guid Id { get; set; }
        public DateTime Recorded { get; set; }
        public string Action { get; set; }   // C/R/U/D/E
        public string Outcome { get; set; }  // 0/4/8/12
        public string AgentUserId { get; set; }
        /// <summary>Sin uso: se decidió no registrar la IP. Queda por la columna existente.</summary>
        public string AgentIp { get; set; }
        public string EntityRef { get; set; }
        public string EntityType { get; set; }
        public string Category { get; set; }
        public string FhirJson { get; set; }
    }
}
