using System;
using Newtonsoft.Json.Linq;

namespace ChequeAR.Application.Services
{
    /// Lee datos demográficos del paciente embebido (recurso Patient "contained")
    /// dentro del FhirJson de un QuestionnaireResponse. Se usa en modo degradado,
    /// cuando el hospital (FHIR/Oracle) no está disponible y la única fuente de
    /// pacientes son las respuestas ya cargadas en ChequeAR.
    public class PatientDemographics
    {
        public string Name { get; set; }
        public string Gender { get; set; }
        public DateTime? BirthDate { get; set; }

        public string AgeLabel
        {
            get
            {
                if (!BirthDate.HasValue) return null;
                var today = ChequeAR.Application.Reports.ArgentinaClock.FromUtc(DateTime.UtcNow).Date;
                var years = today.Year - BirthDate.Value.Year;
                if (BirthDate.Value.Date > today.AddYears(-years)) years--;
                return years + " años";
            }
        }
    }

    public static class FhirPatientReader
    {
        /// Devuelve el nombre para mostrar (subject.display o contained Patient.name),
        /// o null si no se puede extraer.
        public static string ReadName(string fhirJson)
        {
            return Read(fhirJson)?.Name;
        }

        public static PatientDemographics Read(string fhirJson)
        {
            if (string.IsNullOrWhiteSpace(fhirJson)) return null;
            try
            {
                var root = JObject.Parse(fhirJson);
                var demo = new PatientDemographics();

                // 1) subject.display como nombre de fallback rápido
                demo.Name = (string)root.SelectToken("subject.display");

                // 2) recurso Patient "contained" (fuente rica: nombre, nacimiento, sexo)
                var contained = root["contained"] as JArray;
                if (contained != null)
                {
                    foreach (var c in contained)
                    {
                        if ((string)c["resourceType"] == "Patient")
                        {
                            var nameText = (string)c.SelectToken("name[0].text");
                            if (!string.IsNullOrEmpty(nameText)) demo.Name = nameText;

                            demo.Gender = (string)c["gender"];
                            var bd = (string)c["birthDate"];
                            if (!string.IsNullOrEmpty(bd) && DateTime.TryParse(bd, out var parsed))
                                demo.BirthDate = parsed;
                            break;
                        }
                    }
                }

                return demo;
            }
            catch
            {
                return null;
            }
        }
    }
}
