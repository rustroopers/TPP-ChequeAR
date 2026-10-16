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
    ///
    /// A cada pregunta se le suman las opciones que tuvo en versiones anteriores y ya no tiene:
    /// las fichas viejas guardaron esos códigos y sin la etiqueta se mostraban crudos.
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

            var porLinkId = new Dictionary<string, Question>();
            var orden = 0;
            foreach (var version in versiones)
            {
                var frozen = FhirQuestionnaireReader.ReadDomain(version.FhirJson);
                foreach (var question in (frozen?.Questions ?? new List<Question>()).OrderBy(x => x.OrderIndex))
                {
                    if (string.IsNullOrEmpty(question.LinkId)) continue;
                    if (porLinkId.TryGetValue(question.LinkId, out var vigenteDeEsta))
                    {
                        SumarOpcionesRetiradas(vigenteDeEsta, question);
                        continue;
                    }
                    question.GroupId = null;
                    question.OrderIndex = orden++;
                    porLinkId[question.LinkId] = question;
                    result.Questions.Add(question);
                }
            }
            return result;
        }

        private static void SumarOpcionesRetiradas(Question destino, Question anterior)
        {
            if (destino.Options == null) destino.Options = new List<AnswerOption>();
            var codigos = new HashSet<string>(destino.Options.Select(o => o.Code).Where(c => c != null));
            foreach (var opcion in anterior.Options ?? new List<AnswerOption>())
                if (opcion.Code != null && codigos.Add(opcion.Code)) destino.Options.Add(opcion);
        }
    }
}
