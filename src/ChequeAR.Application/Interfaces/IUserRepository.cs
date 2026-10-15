using System.Collections.Generic;
using System.Threading.Tasks;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Application.Interfaces
{
    public interface IUserRepository
    {
        Task<User> GetAsync(string id);

        /// <summary>
        /// SOLO campos escalares (DisplayName/Email/IsActive). Ignora Roles/Specialties
        /// aunque vengan cargadas en `u`: EF6 (CurrentValues.SetValues) no toca colecciones
        /// de navegación, y este método existe justamente para la edición administrativa
        /// (UsersController.Save) que no toca membresías. Para altas/bajas de rol o
        /// especialidad, usar SyncFromHospitalAsync o SetRoleAsync.
        /// PasswordHash/PasswordUpdatedAt TAMPOCO se tocan nunca acá, sin importar lo que
        /// traiga `u`: cambiar una contraseña no es una edición administrativa genérica.
        /// </summary>
        Task UpsertAsync(User u);

        /// <summary>
        /// Reconcilia roles y especialidades del usuario contra lo informado (por el hospital
        /// en el login, o por un admin en el ABM), dando de alta y de baja lo que corresponda
        /// (ver UserMirrorDiff). Crea el usuario si no existe. Actualiza LastLoginAt/LastSyncAt.
        /// </summary>
        Task SyncFromHospitalAsync(string userId, string displayName, string email,
            IReadOnlyList<string> roles, IReadOnlyList<string> specialties);

        /// <summary>Devuelve el sello de seguridad del usuario, generándolo si todavía no tiene uno.</summary>
        Task<string> EnsureSecurityStampAsync(string userId);

        /// <summary>Nuevo sello y revocación de los refresh tokens: cierra todas las sesiones.</summary>
        Task RotateSecurityStampAsync(string userId);

        /// <summary>Ver LoginLockoutPolicy.</summary>
        Task SetLoginStateAsync(string userId, int failedLoginCount, System.DateTime? lockoutUntil);

        /// <summary>Incremento atómico en la base; devuelve el total.</summary>
        Task<int> IncrementFailedLoginAsync(string userId);

        Task SetLockoutUntilAsync(string userId, System.DateTime lockoutUntil);

        Task SetPasswordHashAsync(string userId, string passwordHash);

        Task<IReadOnlyList<User>> ListAsync(string searchTerm, string roleFilter, string specialtyFilter, bool? active, int skip, int take);
        Task<int> CountAsync(string searchTerm, string roleFilter, string specialtyFilter, bool? active);
    }
}
