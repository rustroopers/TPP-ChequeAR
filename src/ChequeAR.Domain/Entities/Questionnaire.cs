using System;
using System.Collections.Generic;
using System.Linq;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Domain.Entities
{
    public class Questionnaire
    {
        public Guid Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public QuestionnaireStatus Status { get; set; }
        public string SpecialtyCode { get; set; }
        public string SpecialtyDisplay { get; set; }

        /// <summary>
        /// Codificación clínica del cuestionario como un todo (concepto SNOMED CT) más el
        /// término de autoría, en el mismo contrato JSON que <see cref="Question.CodingJson"/>:
        /// { system, code, display, term }. Es el análogo a nivel cuestionario de lo que ya se
        /// podía anotar por pregunta: qué mide la ficha completa —ej. "control cardiológico"—,
        /// independiente del código de cada pregunta, que se conserva intacto.
        /// </summary>
        public string CodingJson { get; set; }

        public Guid? CurrentVersionId { get; set; }
        public string CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool IsDeleted { get; set; }

        /// <summary>
        /// Se editó después de publicar y todavía no se publicó una versión con esos
        /// cambios. Es un flag explícito y no una comparación UpdatedAt &gt; PublishedAt
        /// porque en Oracle esas columnas son DATE (precisión de 1 segundo): una edición
        /// dentro del mismo segundo que la publicación quedaba invisible.
        /// </summary>
        public bool HasPendingChanges { get; set; }

        /// <summary>
        /// Contador de escrituras sobre el registro, para concurrencia optimista. Se
        /// incrementa en cada modificación y viaja al cliente como ETag; el cliente lo
        /// devuelve en If-Match y el server rechaza la escritura si no coincide.
        ///
        /// Es el equivalente de Resource.meta.versionId de FHIR (la versión del registro),
        /// distinto de QuestionnaireVersion, que son los snapshots publicados.
        /// Oracle no tiene rowversion, así que lo lleva la aplicación.
        /// </summary>
        public int ConcurrencyVersion { get; set; }

        public virtual ICollection<QuestionnaireVersion> Versions { get; set; } = new List<QuestionnaireVersion>();
        public virtual ICollection<QuestionGroup> Groups { get; set; } = new List<QuestionGroup>();
        public virtual ICollection<Question> Questions { get; set; } = new List<Question>();

        /// <summary>
        /// Publicable si tiene al menos una pregunta y hay algo que publicar:
        ///  - Borrador: se publica su primera versión.
        ///  - Archivado: publicarlo lo saca del archivo y lo vuelve a dejar activo.
        ///  - Activo con cambios pendientes: congela una versión nueva (v2, v3…).
        ///  - Activo sin cambios pendientes: no hay nada nuevo que congelar -> no se publica.
        /// </summary>
        public bool CanPublish()
        {
            if (Questions == null || Questions.Count == 0) return false;
            if (Status == QuestionnaireStatus.Active && !HasPendingChanges) return false;
            return true;
        }

        /// <summary>
        /// Un cuestionario publicado que se siguió editando: la versión congelada sigue
        /// sirviendo a los clínicos (las respuestas apuntan a QuestionnaireVersionId) y los
        /// cambios quedan pendientes hasta la próxima publicación. En un borrador el flag
        /// no significa nada: todavía no hay versión publicada con la que contrastar.
        /// </summary>
        public bool HasUnpublishedChanges()
        {
            return Status == QuestionnaireStatus.Active && HasPendingChanges;
        }

        /// <summary>
        /// La versión vigente: la que apunta <see cref="CurrentVersionId"/> y, si quedó
        /// colgado, la publicada más alta. Null si nunca se publicó.
        /// </summary>
        public QuestionnaireVersion CurrentVersion()
        {
            if (Versions == null || Versions.Count == 0) return null;
            if (CurrentVersionId.HasValue)
            {
                var current = Versions.FirstOrDefault(v => v.Id == CurrentVersionId.Value);
                if (current != null) return current;
            }
            return Versions.OrderByDescending(v => v.VersionNumber).First();
        }

        /// <summary>
        /// Una versión publicada por su id. Null si no existe o si el id es vacío (fichas
        /// anteriores al anclaje de versión).
        /// </summary>
        public QuestionnaireVersion FindVersion(Guid versionId)
        {
            if (Versions == null || versionId == Guid.Empty) return null;
            return Versions.FirstOrDefault(v => v.Id == versionId);
        }

        /// <summary>
        /// La versión pedida si existe; si no, la vigente. Una versión que ya no es la vigente
        /// sigue siendo válida: es el caso de la ficha empezada antes de la última publicación.
        /// </summary>
        public QuestionnaireVersion VersionOrCurrent(Guid versionId) => FindVersion(versionId) ?? CurrentVersion();
    }
}
