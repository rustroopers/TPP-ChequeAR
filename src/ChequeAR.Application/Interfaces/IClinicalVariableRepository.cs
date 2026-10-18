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

        /// <summary>
        /// Las activas y publicadas con ese concepto. El concepto es único sólo entre ellas: una
        /// variable que reemplaza a otra puede repetirlo si la anterior se da de baja.
        /// </summary>
        Task<IReadOnlyList<ClinicalVariable>> GetPublishedActiveBySnomedCodeAsync(string snomedCode);

        /// <summary>Id y nombre de las activas y publicadas, para validar que el nombre no se repita.</summary>
        Task<IReadOnlyList<ClinicalVariable>> GetPublishedActiveNamesAsync();

        /// <summary>Variables publicadas que reemplazan a alguna de <paramref name="ids"/>.</summary>
        Task<IReadOnlyList<ClinicalVariable>> GetSuccessorsAsync(IEnumerable<Guid> ids);

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
        /// <param name="publishedOnly">Sólo las publicadas: lo que puede usar un cuestionario.</param>
        Task<IReadOnlyList<ClinicalVariable>> ListAsync(string search, QuestionType? type, bool? active, int skip, int take, bool publishedOnly = false);

        Task<int> CountAsync(string search, QuestionType? type, bool? active, bool publishedOnly = false);

        Task AddAsync(ClinicalVariable v);

        /// <summary>Reemplaza la variable y todas sus opciones en una transacción.</summary>
        Task UpdateWithOptionsAsync(ClinicalVariable v, ICollection<VariableOption> options);

        /// <summary>Guarda sólo la cabecera (estado), sin tocar las opciones.</summary>
        Task UpdateHeaderAsync(ClinicalVariable v);

        /// <summary>
        /// Guarda la variable recién publicada. Con <paramref name="deactivate"/>, primero da de
        /// baja esa, en la misma transacción: el índice único de SNOMED entre activas no admite
        /// las dos activas ni un instante.
        /// </summary>
        Task PublishAsync(ClinicalVariable v, ClinicalVariable deactivate = null);

        Task DeleteAsync(Guid id);

        /// <summary>Cuántas preguntas la referencian. Decide si una baja borra o desactiva.</summary>
        Task<int> CountQuestionsUsingAsync(Guid variableId);

        /// <summary>Cuestionarios que la usan, para poder decir dónde antes de dar de baja.</summary>
        Task<IReadOnlyList<Guid>> QuestionnaireIdsUsingAsync(Guid variableId);
    }
}
