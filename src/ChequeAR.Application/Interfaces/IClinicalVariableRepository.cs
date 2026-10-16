using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Application.Interfaces
{
    /// <summary>
    /// Acceso al catálogo de variables clínicas. Declarada acá (Application) e implementada en
    /// Infrastructure, como el resto: la capa de negocio define qué necesita, no cómo se guarda.
    /// </summary>
    public interface IClinicalVariableRepository
    {
        Task<ClinicalVariable> GetAsync(Guid id);
        Task<ClinicalVariable> GetByCodeAsync(string code);
        Task<ClinicalVariable> GetBySnomedCodeAsync(string snomedCode);

        /// <summary>
        /// Trae varias de una. Es lo que usa QuestionnaireService al guardar un cuestionario:
        /// una sola consulta para todas las preguntas, no una por pregunta.
        /// </summary>
        Task<IReadOnlyList<ClinicalVariable>> GetManyAsync(IEnumerable<Guid> ids);

        /// <summary>
        /// Búsqueda del catálogo y del selector del editor. <paramref name="search"/> matchea
        /// contra nombre, código y concepto SNOMED: el buscador es un solo campo.
        /// <paramref name="active"/>: null = todas, true = sólo activas, false = sólo de baja.
        /// </summary>
        /// <param name="publishedOnly">Sólo las que tienen versión publicada: lo que puede usar un cuestionario.</param>
        Task<IReadOnlyList<ClinicalVariable>> ListAsync(string search, QuestionType? type, bool? active, int skip, int take, bool publishedOnly = false);

        Task<int> CountAsync(string search, QuestionType? type, bool? active, bool publishedOnly = false);

        Task AddAsync(ClinicalVariable v);

        /// <summary>Reemplaza la variable y todas sus opciones en una transacción.</summary>
        Task UpdateWithOptionsAsync(ClinicalVariable v, ICollection<VariableOption> options);

        /// <summary>Guarda sólo la cabecera (estado, versión vigente), sin tocar las opciones.</summary>
        Task UpdateHeaderAsync(ClinicalVariable v);

        /// <summary>Agrega una versión publicada y guarda la cabecera en la misma operación.</summary>
        Task AddVersionAsync(ClinicalVariable v, ClinicalVariableVersion version);

        Task<ClinicalVariableVersion> GetVersionAsync(Guid versionId);
        Task<ClinicalVariableVersion> GetVersionAsync(Guid variableId, int versionNumber);

        /// <summary>Historial sin la definición (no trae el CLOB), de la más nueva a la más vieja.</summary>
        Task<IReadOnlyList<ClinicalVariableVersion>> ListVersionsAsync(Guid variableId);

        /// <summary>Todas las versiones de varias variables, con su definición: para armar un cuestionario de una vez.</summary>
        Task<IReadOnlyList<ClinicalVariableVersion>> GetVersionsAsync(IEnumerable<Guid> variableIds);

        /// <summary>Número y revisión de origen de la versión vigente de cada variable, sin la definición.</summary>
        Task<IReadOnlyDictionary<Guid, ClinicalVariableVersion>> GetCurrentVersionHeadersAsync(IEnumerable<Guid> variableIds);

        /// <summary>La revisión de origen se actualiza si se publica sin cambios: no hay versión nueva que crear.</summary>
        Task UpdateVersionSourceAsync(ClinicalVariableVersion version);

        Task DeleteAsync(Guid id);

        /// <summary>Cuántas preguntas la referencian. Decide si una baja borra o desactiva.</summary>
        Task<int> CountQuestionsUsingAsync(Guid variableId);

        /// <summary>Cuestionarios que la usan, para poder decir dónde antes de dar de baja.</summary>
        Task<IReadOnlyList<Guid>> QuestionnaireIdsUsingAsync(Guid variableId);
    }
}
