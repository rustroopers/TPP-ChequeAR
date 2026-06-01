using System.Threading.Tasks;
using ChequeAR.Application.Dtos;

namespace ChequeAR.Application.Interfaces
{
    public interface IHospitalAuthClient
    {
        Task<UserInfo> LoginAsync(string username, string password);
    }
}
