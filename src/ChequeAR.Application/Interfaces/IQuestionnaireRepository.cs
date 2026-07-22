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
        /// <summary>Reemplaza secciones/preguntas/opciones del cuestionario por las recibidas.</summary>
        Task ReplaceStructureAsync(Questionnaire q, ICollection<QuestionGroup> groups, ICollection<Question> questions);
        Task SoftDeleteAsync(Guid id);
        Task AddVersionAsync(QuestionnaireVersion v);
        Task<QuestionnaireVersion> GetVersionAsync(Guid questionnaireId, int versionNumber);
    }
}
