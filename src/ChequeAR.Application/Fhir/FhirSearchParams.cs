using System;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Application.Fhir
{
    /// <summary>
    /// Parámetros de búsqueda de QuestionnaireResponse tal como los manda un cliente FHIR. Un
    /// valor que no se entiende se rechaza: ignorarlo devolvía fichas que no cumplían el filtro.
    /// </summary>
    public static class FhirSearchParams
    {
        /// <summary>Código FHIR (in-progress, completed, amended, entered-in-error) o el nombre del enum.</summary>
        public static bool TryParseStatus(string value, out ResponseStatus? status)
        {
            status = null;
            if (string.IsNullOrWhiteSpace(value)) return true;
            switch (value.Trim().ToLowerInvariant())
            {
                case "in-progress": status = ResponseStatus.InProgress; return true;
                case "completed": status = ResponseStatus.Completed; return true;
                case "amended": status = ResponseStatus.Amended; return true;
                case "entered-in-error": status = ResponseStatus.EnteredInError; return true;
            }
            if (Enum.TryParse(value.Trim(), true, out ResponseStatus s) && Enum.IsDefined(typeof(ResponseStatus), s))
            {
                status = s;
                return true;
            }
            return false;
        }

        /// <summary>Id del cuestionario, su canonical (…/Questionnaire/{id}, con o sin |versión) o Questionnaire/{id}.</summary>
        public static bool TryParseQuestionnaire(string value, out Guid? id)
        {
            id = null;
            if (string.IsNullOrWhiteSpace(value)) return true;
            var v = value.Trim();
            var bar = v.IndexOf('|');
            if (bar >= 0) v = v.Substring(0, bar);
            v = v.TrimEnd('/');
            var slash = v.LastIndexOf('/');
            if (slash >= 0) v = v.Substring(slash + 1);
            if (!Guid.TryParse(v, out var g)) return false;
            id = g;
            return true;
        }

        /// <summary>HC del paciente, como "Patient/{hc}" o sola.</summary>
        public static string ParseSubject(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var v = value.Trim();
            return v.StartsWith("Patient/", StringComparison.OrdinalIgnoreCase) ? v.Substring("Patient/".Length) : v;
        }
    }
}
