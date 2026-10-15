using System.Threading.Tasks;
using ChequeAR.Application.Fhir;

namespace ChequeAR.Application.Interfaces
{
    /// <summary>Datos del paciente según el hospital, para embeberlos en la ficha.</summary>
    public interface IPatientDemographicsSource
    {
        /// <summary>Null si el hospital no responde o no conoce al paciente.</summary>
        Task<PatientDemographicsDto> TryGetAsync(string hospitalId);
    }
}
