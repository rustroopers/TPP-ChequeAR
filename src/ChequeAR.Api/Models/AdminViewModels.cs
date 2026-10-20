using System;
using System.Collections.Generic;

namespace ChequeAR.Api.Models
{
    public class AuditEntry
    {
        public Guid Id { get; set; }
        public string Timestamp { get; set; }
        public string Username { get; set; }
        public string Action { get; set; }        // etiqueta legible en español
        public string ActionBadge { get; set; }   // clase css del badge (color)
        public string ActionIcon { get; set; }    // clase del ícono bootstrap
        public string Resource { get; set; }       // tipo de recurso, en español
        public string ResourceName { get; set; }   // nombre del recurso puntual, o "—"
    }

    public class AuditListViewModel
    {
        public string UserFilter { get; set; }
        public string ActionFilter { get; set; }
        /// <summary>Código del tipo de recurso, ej. "ClinicalVariable".</summary>
        public string ResourceTypeFilter { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public List<AuditEntry> Entries { get; set; } = new List<AuditEntry>();
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    public class AuditDetailViewModel
    {
        public Guid Id { get; set; }
        public string Timestamp { get; set; }
        public string Username { get; set; }
        public string FullName { get; set; }
        public string Action { get; set; }
        public string ActionBadge { get; set; }
        public string ActionIcon { get; set; }
        public string Resource { get; set; }
        public string ResourceName { get; set; }

        // Diff de versiones (solo para publicaciones de un cuestionario).
        public bool HasVersionDiff { get; set; }
        public int? FromVersion { get; set; }   // null si fue la primera versión
        public int? ToVersion { get; set; }
        public List<Helpers.AuditPresentation.DiffLine> BeforeLines { get; set; } = new List<Helpers.AuditPresentation.DiffLine>();
        public List<Helpers.AuditPresentation.DiffLine> AfterLines { get; set; } = new List<Helpers.AuditPresentation.DiffLine>();

        // Diff de lo completado en una sesión de guardado de una ficha (solo para eventos
        // "$save" de una QuestionnaireResponse que efectivamente cambiaron algo).
        public bool HasResponseDiff { get; set; }
        public List<ChequeAR.Application.Services.ResponseFieldChange> ResponseChanges { get; set; }
            = new List<ChequeAR.Application.Services.ResponseFieldChange>();

        // Foto completa de la ficha al finalizarla (todas las preguntas + respuesta final).
        public bool HasResponseSnapshot { get; set; }
        public List<ChequeAR.Application.Services.ResponseSnapshotSection> ResponseSnapshot { get; set; }
            = new List<ChequeAR.Application.Services.ResponseSnapshotSection>();

        // Foto de una consulta: reporte guardado (creado / modificado / eliminado), reporte
        // exportado, evolución de un paciente o exportación de la auditoría.
        public bool HasReportDetail { get; set; }
        public string ReportHeading { get; set; } = "Reporte";
        public string ConditionsHeading { get; set; } = "Condiciones";
        public string NoConditionsText { get; set; } = "Sin filtros: incluye todas las fichas del cuestionario.";
        public string ReportName { get; set; }               // sólo reportes guardados
        public string ReportScope { get; set; }              // etiqueta legible del alcance; sólo reportes guardados
        public string ReportQuestionnaire { get; set; }      // título del cuestionario base
        public List<string> ReportConditions { get; set; } = new List<string>();
        public List<string> PreviousReportConditions { get; set; }   // sólo si la modificación cambió las condiciones

        // Qué cambió en una edición (usuario, variable, cuestionario o consulta guardada).
        public List<ChequeAR.Application.Services.AuditFieldChange> FieldChanges { get; set; }
            = new List<ChequeAR.Application.Services.AuditFieldChange>();
        public List<string> RemovedLines { get; set; } = new List<string>();
        public List<string> AddedLines { get; set; } = new List<string>();
        public bool HasChanges => FieldChanges.Count > 0 || RemovedLines.Count > 0 || AddedLines.Count > 0;
    }

    public class UserListItem
    {
        public string Username { get; set; }
        public string FullName { get; set; }
        public string Role { get; set; }
        public string Specialty { get; set; }
        public bool Active { get; set; }
    }

    public class UserListViewModel
    {
        public List<UserListItem> Users { get; set; } = new List<UserListItem>();

        public string Query { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
        public int Total { get; set; }
    }

    public class UserDetailViewModel
    {
        [System.ComponentModel.DataAnnotations.Required]
        public string Username { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }
        public bool Active { get; set; }

        /// <summary>Rol único y global del usuario (Administrador/Jefe/Medico). Ver Domain.Enums.Role.</summary>
        [System.ComponentModel.DataAnnotations.Required]
        public string Role { get; set; }

        public int Revision { get; set; }
        public List<UserSpecialtyItem> Specialties { get; set; } = new List<UserSpecialtyItem>();

        /// <summary>Códigos marcados en el formulario al guardar.</summary>
        public List<string> SpecialtyCodes { get; set; }
    }

    /// <summary>
    /// Una especialidad asignada a un usuario. Ya NO lleva un rol propio: el rol es único y
    /// global por usuario (ver UserDetailViewModel.Role) -- antes esta clase se llamaba
    /// UserSpecialtyRole y prometía un vocabulario de roles por especialidad que el modelo de
    /// datos nunca soportó (Save descartaba en silencio lo que se posteara acá).
    /// </summary>
    public class UserSpecialtyItem
    {
        public string SpecialtyCode { get; set; }
        public string SpecialtyLabel { get; set; }
        public string SpecialtyColor { get; set; }
    }

    public class CapabilityStatementViewModel
    {
        public string FhirVersion { get; set; }
        public string Software { get; set; }
        public string Implementation { get; set; }
        public List<CapResource> Resources { get; set; } = new List<CapResource>();
        public List<string> Profiles { get; set; } = new List<string>();
    }

    public class CapResource
    {
        public string Type { get; set; }
        public string Interactions { get; set; }

        /// <summary>
        /// Un recurso declarado SIN interacciones significa "conozco este tipo y NO lo
        /// soporto". Sin distinguirlo, la pantalla lo mostraba igual que a uno soportado
        /// pero con la columna de operaciones vacía, que se lee como un error de la pantalla
        /// en vez de como una decisión.
        /// </summary>
        public bool Soportado { get; set; } = true;

        /// <summary>Nota para quien integra: por qué no se soporta, o de dónde salen los datos.</summary>
        public string Nota { get; set; }
    }
}
