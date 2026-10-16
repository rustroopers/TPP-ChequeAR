using System;
using ChequeAR.Application.Dtos;
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

        /// <summary>Igual, pero sólo de esas especialidades, filtrado y paginado en la base.</summary>
        Task<IReadOnlyList<Questionnaire>> SearchAsync(QuestionnaireStatus? status, string titleContains, string specialtyCode,
            IReadOnlyCollection<string> withinSpecialties, int skip, int take);
        Task<int> CountAsync(QuestionnaireStatus? status, string titleContains, string specialtyCode,
            IReadOnlyCollection<string> withinSpecialties);

        /// <summary>Sólo las columnas del cuestionario, sin versiones ni estructura.</summary>
        Task<IReadOnlyList<Questionnaire>> GetHeadersAsync(IEnumerable<Guid> ids);

        /// <summary>
        /// Cantidad de versiones publicadas por cuestionario, en UNA consulta. Los listados la
        /// necesitan por fila, y leer Questionnaire.Versions de cada resultado de SearchAsync
        /// dispara una carga diferida por fila (y trae el FhirJson de cada versión).
        /// </summary>
        Task<IReadOnlyDictionary<Guid, int>> GetVersionCountsAsync(IEnumerable<Guid> ids);

        /// <summary>
        /// Los cuestionarios pedidos con secciones, preguntas y opciones ya cargadas, en el
        /// orden de <paramref name="ids"/>. Para armar varios recursos FHIR completos (el
        /// Bundle de búsqueda) sin una carga diferida por colección y por fila. No trae las
        /// versiones: para el número está <see cref="GetVersionCountsAsync"/>.
        /// </summary>
        Task<IReadOnlyList<Questionnaire>> GetManyWithStructureAsync(IReadOnlyList<Guid> ids);

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

        /// <summary>
        /// Una versión publicada por id, sin cargar el resto del cuestionario. Null si no existe
        /// o no es de ese cuestionario.
        /// </summary>
        Task<QuestionnaireVersion> GetVersionByIdAsync(Guid questionnaireId, Guid versionId);

        /// <summary>Títulos por id, para resolver referencias sin traer el agregado completo.</summary>
        Task<IReadOnlyDictionary<Guid, string>> GetTitlesAsync(IEnumerable<Guid> ids);

        /// <summary>
        /// Id y título de todos los cuestionarios, ordenados por título. Incluye los
        /// eliminados: la auditoría tiene que poder filtrar por ellos.
        /// </summary>
        Task<IReadOnlyList<QuestionnaireTitle>> ListTitlesAsync();

        /// <summary>Códigos de especialidad realmente presentes en los cuestionarios (para poblar el filtro).</summary>
        Task<IReadOnlyList<string>> GetSpecialtyCodesAsync();

        /// <summary>
        /// Sólo el SpecialtyCode, sin traer el agregado completo (Versions/Groups/Questions/
        /// Options). Para chequear el scope de especialidad antes de operar sobre una
        /// QuestionnaireResponse sin pagar el costo de GetAsync. Null si no existe.
        /// </summary>
        Task<string> GetSpecialtyCodeAsync(Guid questionnaireId);
    }
}
