using System.Collections.Generic;
using System.Linq;
using ChequeAR.Application.Fhir;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Application.Reports
{
    /// <summary>
    /// Las preguntas que tuvo el cuestionario en sus versiones publicadas: las de la vigente, en
    /// su orden y con su texto, y después las que se retiraron, con la última definición que
    /// tuvieron. Es contra lo que se cargaron las fichas; el borrador puede tener preguntas que
    /// todavía no respondió nadie y no tener otras que sí tienen datos.
    /// </summary>
    public static class PublishedStructure
    {
        public static Questionnaire Union(Questionnaire q)
        {
            var result = new Questionnaire { Id = q.Id, Title = q.Title, SpecialtyCode = q.SpecialtyCode };
            var vigente = q.CurrentVersion();
            var versiones = (q.Versions ?? new List<QuestionnaireVersion>())
                .OrderByDescending(v => v.Id == vigente?.Id)
                .ThenByDescending(v => v.VersionNumber);

            var vistas = new HashSet<string>();
            var orden = 0;
            foreach (var version in versiones)
            {
                var frozen = FhirQuestionnaireReader.ReadDomain(version.FhirJson);
                foreach (var question in (frozen?.Questions ?? new List<Question>()).OrderBy(x => x.OrderIndex))
                {
                    if (string.IsNullOrEmpty(question.LinkId) || !vistas.Add(question.LinkId)) continue;
                    question.GroupId = null;
                    question.OrderIndex = orden++;
                    result.Questions.Add(question);
                }
            }
            return result;
        }
    }
}
