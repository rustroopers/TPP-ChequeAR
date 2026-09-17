using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Dtos;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Infrastructure.Auth
{
    /// Stand-in del login del hospital mientras ese servicio no existe. Se registra
    /// SOLO cuando HospitalAuth:LoginUrl esta vacio (ver ServiceComposition), misma
    /// convencion que Terminology:BaseUrl: sin URL configurada no hay integracion.
    ///
    /// ACEPTA CUALQUIER CONTRASENA. El rol sale del prefijo del usuario, para poder
    /// probar la UI de cada perfil sin backend:
    ///   admin*  -> Administrador   jefe* -> Jefe
    ///   obs*    -> Observador      resto -> Medico
    /// Al cablear el hospital real, borrar esta clase y dejar de registrarla.
    ///
    /// Las ESPECIALIDADES ya no salen vacias: se leen del espejo local (UserSpecialty),
    /// sembrado por Migrations/Configuration.Seed() para los usuarios de demo. Un usuario
    /// que no existe en el espejo (inventado en el momento, el mock acepta cualquiera)
    /// entra con cero especialidades -- probar el fail-closed no deberia requerir
    /// preparar nada de antemano.
    public class MockHospitalAuthClient : IHospitalAuthClient
    {
        private readonly IUserRepository _users;

        public MockHospitalAuthClient(IUserRepository users)
        {
            _users = users;
        }

        public async Task<UserInfo> LoginAsync(string username, string password)
        {
            // Usuario vacio sigue siendo un login invalido: el mock afloja la
            // verificacion de identidad, no la forma del contrato.
            if (string.IsNullOrWhiteSpace(username)) return null;

            var user = username.Trim();

            var espejo = await _users.GetAsync(user);
            if (espejo != null)
            {
                var roles = (espejo.Roles ?? new List<Domain.Entities.UserRole>()).Select(r => r.Role).ToList();
                return new UserInfo
                {
                    Id = espejo.Id,
                    DisplayName = espejo.DisplayName ?? user,
                    Roles = roles.Count > 0 ? roles : RolesFor(user),
                    Specialties = (espejo.Specialties ?? new List<Domain.Entities.UserSpecialty>())
                        .Select(s => s.SpecialtyCode).ToList()
                };
            }

            return new UserInfo
            {
                Id = user,
                DisplayName = user,
                Roles = RolesFor(user),
                Specialties = new List<string>()
            };
        }

        private static List<string> RolesFor(string username)
        {
            // Jerarquico: quien administra tambien puede lo de los perfiles de abajo,
            // que es como los chequean los endpoints (rol requerido, no rol exacto).
            if (username.StartsWith("admin", StringComparison.OrdinalIgnoreCase))
                return new List<string> { Role.Administrador, Role.Jefe, Role.Medico };

            if (username.StartsWith("jefe", StringComparison.OrdinalIgnoreCase))
                return new List<string> { Role.Jefe, Role.Medico };

            if (username.StartsWith("obs", StringComparison.OrdinalIgnoreCase))
                return new List<string> { Role.Observador };

            return new List<string> { Role.Medico };
        }
    }
}
