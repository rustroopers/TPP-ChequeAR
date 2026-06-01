using System.Collections.Generic;

namespace ChequeAR.Application.Interfaces
{
    public interface IFhirValidator
    {
        ValidationResult Validate(string fhirJson, string profileUrl = null);
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public IReadOnlyList<string> Errors { get; set; }
        public IReadOnlyList<string> Warnings { get; set; }
    }
}
