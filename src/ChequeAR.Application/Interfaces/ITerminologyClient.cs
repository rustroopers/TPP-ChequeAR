using System.Threading.Tasks;

namespace ChequeAR.Application.Interfaces
{
    /// Servidor terminológico FHIR (Snowstorm) para autocompletar códigos SNOMED CT.
    /// Devuelve el JSON crudo de la respuesta ValueSet/$expand; el moldeo a
    /// {code, display} lo hace la capa API (que ya tiene Newtonsoft), igual que
    /// IHospitalFhirClient. Así Infrastructure no depende de un parser de JSON.
    public interface ITerminologyClient
    {
        /// filter = texto a buscar (el texto de la pregunta). count = máximo de resultados.
        Task<string> ExpandSnomedAsync(string filter, int count);
    }
}
