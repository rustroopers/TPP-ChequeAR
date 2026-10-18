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

        /// <summary>Varias fichas por id, sin sus ítems: sólo cuestionario y paciente. Las que no existen no vienen.</summary>
        Task<IReadOnlyList<QuestionnaireResponse>> GetHeadersAsync(IReadOnlyCollection<Guid> ids);

        /// <param name="scope">
        /// Restringe por la especialidad del Questionnaire al que pertenece cada respuesta
        /// (join, QuestionnaireResponse no tiene columna de especialidad propia — ver C2 en
        /// specs/011). Null = sin restricción (las vistas de paciente, fuera de alcance de
        /// esta feature, no lo pasan y conservan el comportamiento de siempre).
        /// </param>
        Task<IReadOnlyList<QuestionnaireResponse>> SearchAsync(string patientHospitalId, Guid? questionnaireId, ResponseStatus? status, int skip, int take, SpecialtyScope scope = null);
        Task AddAsync(QuestionnaireResponse r);
        Task UpdateAsync(QuestionnaireResponse r);
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

        /// <summary>Reemplaza todas las respuestas de la ficha y guarda la cabecera en la misma operación.</summary>
        Task ReplaceItemsAsync(QuestionnaireResponse r, ICollection<QuestionnaireResponseItem> items);

        /// <summary>Descarta lo leído y lo pendiente, para volver a leer de la base después de un conflicto.</summary>
        void DiscardChanges();
        /// <summary>Cuestionarios de las fichas que referencian el adjunto.</summary>
        Task<IReadOnlyList<Guid>> FindQuestionnaireIdsByAttachmentAsync(Guid attachmentId);
        /// <summary>¿Hay respuestas cargadas contra este cuestionario? Decide si se puede borrar.</summary>
        Task<bool> HasResponsesAsync(Guid questionnaireId);

        /// <summary>
        /// Cantidad de fichas que matchean los mismos filtros que <see cref="SearchAsync"/>, sin
        /// paginar (un COUNT en la base, sin traerlas). Sin filtros, el total de fichas.
        /// </summary>
        Task<int> CountAsync(string patientHospitalId = null, Guid? questionnaireId = null, ResponseStatus? status = null, SpecialtyScope scope = null);
    }
}
