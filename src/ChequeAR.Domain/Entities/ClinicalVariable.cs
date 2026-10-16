using System;
using System.Collections.Generic;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Domain.Entities
{
    /// <summary>
    /// Variable clínica del catálogo: la unidad de vocabulario con la que se arman los
    /// cuestionarios. Antes de esta feature cada jefe escribía sus preguntas a mano, así que
    /// "Peso" y "peso corporal" convivían y sólo una de las dos tenía código SNOMED. Ahora el
    /// Administrador define la variable una vez -tipo, unidad, rango, opciones y concepto
    /// SNOMED- y el Jefe la usa; del cuestionario sólo puede ajustar el enunciado, si es
    /// requerida y su enableWhen.
    ///
    /// Unidad, rango y coding van en columnas tipadas y NO en un CLOB como ValidationJson /
    /// CodingJson de Question: el catálogo es la fuente de verdad y necesita índice único por
    /// SnomedCode, y en Oracle un CLOB no entra en un índice ni en un WHERE. Esos dos JSON se
    /// *renderizan* al snapshot de la pregunta (ver ClinicalVariableSnapshot), de modo que el
    /// contrato que leen QuestionMetadataReader y el par builder/parser FHIR no cambia.
    /// </summary>
    public class ClinicalVariable
    {
        public Guid Id { get; set; }

        /// <summary>
        /// Slug estable y único ("peso", "ta-sistolica"). Es la clave de negocio -la que el
        /// editor usa para derivar el LinkId de la pregunta- y por eso no se deriva del Name:
        /// renombrar una variable no debe mover la columna sobre la que pivotean los reportes.
        /// </summary>
        public string Code { get; set; }

        /// <summary>Nombre corto con el que aparece en el catálogo y en el buscador ("Peso").</summary>
        public string Name { get; set; }

        /// <summary>
        /// Enunciado que se propone al insertarla en un cuestionario. Es sólo el punto de
        /// partida: el jefe lo sobrescribe para decir "Peso al nacer" o "Peso al día de hoy".
        /// </summary>
        public string DefaultText { get; set; }

        /// <summary>Ayuda para quien elige la variable; no viaja al cuestionario.</summary>
        public string Description { get; set; }

        public QuestionType Type { get; set; }

        /// <summary>
        /// Bloqueado igual que Type: que una variable admita repetición es estructural del
        /// concepto, no una decisión de cada cuestionario.
        /// </summary>
        public bool RepeatsAllowed { get; set; }

        /// <summary>Unidad UCUM ("kg", "cm", "mmHg"). Sólo para tipos numéricos.</summary>
        public string Unit { get; set; }

        public decimal? MinValue { get; set; }
        public decimal? MaxValue { get; set; }

        /// <summary>Largo máximo; sólo para String y Text.</summary>
        public int? MaxLength { get; set; }


        /// <summary>Lista de tipos MIME aceptados; sólo para Attachment.</summary>
        public string Accept { get; set; }

        /// <summary>Sistema de codificación; por defecto TerminologyService.SnomedSystem.</summary>
        public string SnomedSystem { get; set; }

        /// <summary>
        /// Concepto SNOMED CT. Único entre variables activas e inactivas, pero nullable: una
        /// variable puede existir antes de que se le encuentre el concepto correcto.
        /// </summary>
        public string SnomedCode { get; set; }

        public string SnomedDisplay { get; set; }

        /// <summary>
        /// Término de autoría: cómo lo escribió quien creó la variable, aunque todavía no haya
        /// código. Mismo concepto que la extensión question-term que ya emite el builder FHIR.
        /// </summary>
        public string Term { get; set; }

        /// <summary>
        /// Baja lógica. Una variable inactiva no se puede agregar a un cuestionario nuevo, pero
        /// los que ya la usan siguen funcionando: tienen el snapshot.
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>Orden de presentación en el buscador. Menor primero; empate => Name.</summary>
        public int SortOrder { get; set; }

        /// <summary>
        /// Se incrementa en cada guardado del borrador y es el token de concurrencia: dos
        /// administradores que editan a la vez no se pisan.
        /// </summary>
        public int Revision { get; set; }

        /// <summary>Versión publicada vigente; null si la variable nunca se publicó.</summary>
        public Guid? CurrentVersionId { get; set; }

        public string CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public string UpdatedBy { get; set; }
        public DateTime UpdatedAt { get; set; }

        public virtual ICollection<VariableOption> Options { get; set; } = new List<VariableOption>();

        public virtual ICollection<ClinicalVariableVersion> Versions { get; set; } = new List<ClinicalVariableVersion>();

        /// <summary>Tipos que admiten unidad y rango.</summary>
        public bool IsNumeric() => Type == QuestionType.Decimal || Type == QuestionType.Integer || Type == QuestionType.Scale;

        /// <summary>Tipos que no tienen sentido sin opciones de respuesta.</summary>
        public bool RequiresOptions() => Type == QuestionType.Choice || Type == QuestionType.OpenChoice;

        /// <summary>Tipos que admiten MaxLength.</summary>
        public bool AcceptsMaxLength() => Type == QuestionType.String || Type == QuestionType.Text;
    }
}
