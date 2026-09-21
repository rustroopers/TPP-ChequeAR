using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Dtos;
using ChequeAR.Application.Interfaces;

namespace ChequeAR.Application.Security
{
    /// <summary>
    /// Autenticación local: valida usuario+contraseña contra AppUser.PasswordHash. Es el
    /// ICredentialAuthenticator que se registra mientras HospitalAuth:LoginUrl esté vacío
    /// (ver ServiceComposition) -- reemplaza a MockHospitalAuthClient, que aceptaba
    /// cualquier contraseña.
    ///
    /// Fail-closed UNIFORME: usuario inexistente, inactivo, sin hash, o password incorrecta
    /// devuelven exactamente lo mismo (null), sin distinguir la causa -ni en el valor de
    /// retorno ni en el tiempo de respuesta: se corre un PBKDF2 "dummy" en los caminos
    /// negativos para que no se pueda enumerar qué usuarios existen midiendo latencia.
    /// </summary>
    public class LocalCredentialAuthenticator : ICredentialAuthenticator
    {
        // Hash de una contraseña que nadie va a escribir: existe sólo para que Verify()
        // gaste el mismo PBKDF2 que un intento real, sin que haga falta un usuario real.
        private static readonly string DummyHash = PasswordHasher.Hash(System.Guid.NewGuid().ToString("N"));

        private readonly IUserRepository _users;

        public LocalCredentialAuthenticator(IUserRepository users)
        {
            _users = users;
        }

        public async Task<UserInfo> LoginAsync(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                PasswordHasher.Verify(password, DummyHash);
                return null;
            }

            var user = await _users.GetAsync(username.Trim());
            if (user == null || !user.IsActive || string.IsNullOrEmpty(user.PasswordHash))
            {
                PasswordHasher.Verify(password, DummyHash);
                return null;
            }

            if (!PasswordHasher.Verify(password, user.PasswordHash)) return null;

            return new UserInfo
            {
                Id = user.Id,
                DisplayName = user.DisplayName ?? user.Id,
                Roles = (user.Roles ?? new List<Domain.Entities.UserRole>()).Select(r => r.Role).ToList(),
                Specialties = (user.Specialties ?? new List<Domain.Entities.UserSpecialty>()).Select(s => s.SpecialtyCode).ToList()
            };
        }
    }
}
