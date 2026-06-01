using System;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Application.Services
{
    public static class AuditEventBuilder
    {
        public static AuditEvent For(string action, string entityRef, string userId, string ip = null, string outcome = "0", string category = "rest")
        {
            var actionCode = MapActionCode(action);
            return new AuditEvent
            {
                Id = Guid.NewGuid(),
                Recorded = DateTime.UtcNow,
                Action = actionCode,
                Outcome = outcome,
                AgentUserId = userId,
                AgentIp = ip,
                EntityRef = entityRef,
                EntityType = entityRef != null && entityRef.Contains("/") ? entityRef.Split('/')[0] : null,
                Category = category,
                FhirJson = null
            };
        }

        private static string MapActionCode(string action)
        {
            switch ((action ?? string.Empty).ToLowerInvariant())
            {
                case "create": return "C";
                case "read":   return "R";
                case "update": return "U";
                case "delete": return "D";
                case "execute":
                default:       return "E";
            }
        }
    }
}
