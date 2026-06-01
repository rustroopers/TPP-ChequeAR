using System.Threading.Tasks;

namespace ChequeAR.Application.Interfaces
{
    public interface IHospitalFhirClient
    {
        Task<string> GetPatientAsync(string hospitalId);
        Task<string> GetLastObservationAsync(string hospitalId, string code);
        Task<string> SearchConditionsAsync(string hospitalId);
    }
}
