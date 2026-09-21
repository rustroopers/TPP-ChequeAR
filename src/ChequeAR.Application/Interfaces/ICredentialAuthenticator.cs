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
    /// Antes se llamaba IHospitalAuthClient porque sólo existía la idea de delegar en la API
    /// del hospital (todavía no construida). Ahora tiene dos implementaciones intercambiables:
    /// LocalCredentialAuthenticator (Application, valida contra AppUser.PasswordHash -- el
    /// default de hoy) y HospitalAuthClient (Infrastructure, la puerta futura al hospital,
    /// se activa configurando HospitalAuth:LoginUrl). Ver ServiceComposition.
    /// </summary>
    public interface ICredentialAuthenticator
    {
        Task<UserInfo> LoginAsync(string username, string password);
    }
}
