using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Application.Interfaces
{
    public interface IQuestionnaireRepository
    {
        Task<Questionnaire> GetAsync(Guid id);
        Task<IReadOnlyList<Questionnaire>> SearchAsync(QuestionnaireStatus? status, string titleContains, string specialtyCode, int skip, int take);
        Task<int> CountAsync(QuestionnaireStatus? status, string titleContains, string specialtyCode);
        Task AddAsync(Questionnaire q);
        Task UpdateAsync(Questionnaire q);

        /// <summary>
        /// Cabecera + reemplazo de secciones/preguntas/opciones, en una sola transacción y
        /// con la cabecera primero: si hay conflicto de concurrencia, aborta antes de haber
        /// borrado nada.
        /// </summary>
        Task UpdateWithStructureAsync(Questionnaire q, ICollection<QuestionGroup> groups, ICollection<Question> questions);

        /// <summary>Versión congelada + cabecera en una sola transacción.</summary>
        Task PublishAsync(Questionnaire q, QuestionnaireVersion version);
        Task SoftDeleteAsync(Guid id);
        Task<QuestionnaireVersion> GetVersionAsync(Guid questionnaireId, int versionNumber);
    }
}
