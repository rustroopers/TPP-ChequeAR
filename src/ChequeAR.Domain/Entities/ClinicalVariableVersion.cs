using System;

namespace ChequeAR.Domain.Entities
{
    /// <summary>
    /// Versión publicada de una variable clínica: una foto inmutable de su definición. Las
    /// preguntas se atan a una versión concreta, igual que una ficha se ata a una versión del
    /// cuestionario, así que editar la variable no cambia nada de lo ya armado.
    /// </summary>
    public class ClinicalVariableVersion
    {
        public Guid Id { get; set; }
        public Guid VariableId { get; set; }
        public int VersionNumber { get; set; }

        /// <summary>Revisión del borrador que se publicó; si difiere de la actual, hay cambios sin publicar.</summary>
        public int SourceRevision { get; set; }

        /// <summary>La definición completa, opciones incluidas (ver VariableDefinition).</summary>
        public string DefinitionJson { get; set; }

        public string PublishedBy { get; set; }
        public DateTime PublishedAt { get; set; }
        public string ChangeNote { get; set; }
    }
}
