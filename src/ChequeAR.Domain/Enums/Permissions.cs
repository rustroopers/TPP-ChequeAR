namespace ChequeAR.Domain.Enums
{
    /// <summary>
    /// Catálogo de operaciones protegidas. La matriz rol -&gt; permisos vive en un único lugar:
    /// ChequeAR.Application.Security.RolePolicy. Ningún controller ni servicio debería volver
    /// a escribir "Administrador,Jefe" a mano -- eso es exactamente lo que este catálogo + esa
    /// política reemplazan.
    /// </summary>
    public static class Permissions
    {
        public const string QuestionnaireRead = "questionnaire.read";
        public const string QuestionnaireWrite = "questionnaire.write";
        public const string QuestionnaireDelete = "questionnaire.delete";
        public const string EncounterWrite = "encounter.write";
        public const string ReportRead = "report.read";
        public const string ReportWrite = "report.write";
        public const string AuditRead = "audit.read";
        public const string UserManage = "user.manage";
        public const string IntegrationRead = "integration.read";
        public const string TerminologyExpand = "terminology.expand";
    }
}
