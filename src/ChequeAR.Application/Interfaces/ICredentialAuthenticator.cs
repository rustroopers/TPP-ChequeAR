using System.Threading.Tasks;
using ChequeAR.Application.Dtos;

namespace ChequeAR.Application.Interfaces
{
    /// <summary>
    /// Verifica usuario+contraseña y devuelve la identidad (roles, especialidades) de quien
    /// se autenticó, o null si las credenciales no son válidas -- sin distinguir POR QUÉ
    /// (usuario inexistente, inactivo, password incorrecta): eso es justamente lo que impide
    /// que el mensaje de error revele si un usuario existe.
    ///
    /// La implementación es LocalCredentialAuthenticator, que valida contra
    /// AppUser.PasswordHash: el hospital no delega el login.
    /// </summary>
    public interface ICredentialAuthenticator
    {
        Task<UserInfo> LoginAsync(string username, string password);
    }
}
