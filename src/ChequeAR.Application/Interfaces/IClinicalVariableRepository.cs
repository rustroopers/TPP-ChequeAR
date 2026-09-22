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
        /// </summary>
        Task<IReadOnlyList<ClinicalVariable>> ListAsync(string search, QuestionType? type, bool includeInactive, int skip, int take);

        Task<int> CountAsync(string search, QuestionType? type, bool includeInactive);

        Task AddAsync(ClinicalVariable v);

        /// <summary>Reemplaza la variable y todas sus opciones en una transacción.</summary>
        Task UpdateWithOptionsAsync(ClinicalVariable v, ICollection<VariableOption> options);

        Task DeleteAsync(Guid id);

        /// <summary>Cuántas preguntas la referencian. Decide si una baja borra o desactiva.</summary>
        Task<int> CountQuestionsUsingAsync(Guid variableId);

        /// <summary>Cuestionarios que la usan, para poder decir dónde antes de dar de baja.</summary>
        Task<IReadOnlyList<Guid>> QuestionnaireIdsUsingAsync(Guid variableId);
    }
}
