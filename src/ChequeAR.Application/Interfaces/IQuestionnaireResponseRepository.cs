using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ChequeAR.Application.Security;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Application.Interfaces
{
    public interface IQuestionnaireResponseRepository
    {
        Task<QuestionnaireResponse> GetAsync(Guid id);

        /// <param name="scope">
        /// Restringe por la especialidad del Questionnaire al que pertenece cada respuesta
        /// (join, QuestionnaireResponse no tiene columna de especialidad propia — ver C2 en
        /// specs/011). Null = sin restricción (las vistas de paciente, fuera de alcance de
        /// esta feature, no lo pasan y conservan el comportamiento de siempre).
        /// </param>
        Task<IReadOnlyList<QuestionnaireResponse>> SearchAsync(string patientHospitalId, Guid? questionnaireId, ResponseStatus? status, int skip, int take, SpecialtyScope scope = null);
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
        /// <summary>
        /// Re-anclaje a otra versión: guarda la cabecera (r ya trae la nueva versión/FhirJson/
        /// ConcurrencyVersion) y borra, en la misma transacción, los items cuyo LinkId ya no
        /// existe en la versión nueva (<paramref name="keepLinkIds"/> = los que se conservan).
        /// </summary>
        Task RetargetAsync(QuestionnaireResponse r, ICollection<string> keepLinkIds);
        /// <summary>¿Hay respuestas cargadas contra este cuestionario? Decide si se puede borrar.</summary>
        Task<bool> HasResponsesAsync(Guid questionnaireId);
    }
}
