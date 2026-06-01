using System.Collections.Generic;
using ChequeAR.Application.Interfaces;

namespace ChequeAR.Infrastructure.Fhir
{
    /// <summary>MVP stub. En producción usar Firely Validator (Hl7.Fhir.Specification).</summary>
    public class FhirValidatorService : IFhirValidator
    {
        public ValidationResult Validate(string fhirJson, string profileUrl = null)
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(fhirJson))
                errors.Add("Empty resource");
            else if (!fhirJson.Contains("\"resourceType\""))
                errors.Add("Missing resourceType");

            return new ValidationResult
            {
                IsValid = errors.Count == 0,
                Errors = errors,
                Warnings = new List<string>()
            };
        }
    }
}
