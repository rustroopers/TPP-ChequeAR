using System;
using System.Collections.Generic;
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
    public class MockHospitalAuthClient : IHospitalAuthClient
    {
        public Task<UserInfo> LoginAsync(string username, string password)
        {
            // Usuario vacio sigue siendo un login invalido: el mock afloja la
            // verificacion de identidad, no la forma del contrato.
            if (string.IsNullOrWhiteSpace(username)) return Task.FromResult<UserInfo>(null);

            var user = username.Trim();
            return Task.FromResult(new UserInfo
            {
                Id = user,
                DisplayName = user,
                Roles = RolesFor(user),
                Specialties = new List<string>()
            });
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
