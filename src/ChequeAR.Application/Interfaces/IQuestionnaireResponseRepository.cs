using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Application.Interfaces
{
    public interface IQuestionnaireResponseRepository
    {
        Task<QuestionnaireResponse> GetAsync(Guid id);
        Task<IReadOnlyList<QuestionnaireResponse>> SearchAsync(string patientHospitalId, Guid? questionnaireId, ResponseStatus? status, int skip, int take);
        Task AddAsync(QuestionnaireResponse r);
        Task UpdateAsync(QuestionnaireResponse r);
        Task AddItemsAsync(IEnumerable<QuestionnaireResponseItem> items);
        /// <summary>
        /// Guardado parcial: reemplaza, dentro de la misma transacción que la cabecera
        /// (r ya trae seteados FhirJson/ConcurrencyVersion/lock/etc.), los items cuyo
        /// LinkId aparece en <paramref name="replacementItems"/>. Los LinkId no
        /// mencionados no se tocan, así se preserva lo respondido en llamadas anteriores.
        /// </summary>
        Task SaveProgressAsync(QuestionnaireResponse r, ICollection<QuestionnaireResponseItem> replacementItems);
        /// <summary>¿Hay respuestas cargadas contra este cuestionario? Decide si se puede borrar.</summary>
        Task<bool> HasResponsesAsync(Guid questionnaireId);
    }
}
