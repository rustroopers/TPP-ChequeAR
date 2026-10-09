using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Dtos;
using ChequeAR.Application.Exceptions;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Application.Services
{
    /// <summary>
    /// Gestión del espejo del padrón hospitalario.
    ///
    /// Antes era fina a propósito (sólo delegaba). Ahora es donde vive el sync on-login:
    /// filtra las especialidades que el hospital informa contra el catálogo antes de
    /// escribir el espejo (ver SyncFromHospitalAsync) — con la FK a CHEQUEAR.Specialty, un
    /// código desconocido rompería el login si llegara crudo al repositorio.
    /// </summary>
    public class UserService
    {
        private readonly IUserRepository _repo;
        private readonly SpecialtyCatalogService _catalog;

        public UserService(IUserRepository repo, SpecialtyCatalogService catalog)
        {
            _repo = repo;
            _catalog = catalog;
        }

        public Task<User> GetAsync(string id) => _repo.GetAsync(id);

        public async Task<UserPage> ListAsync(
            string searchTerm = null, string role = null, string specialty = null,
            bool? active = null, int skip = 0, int take = 50)
        {
            var items = await _repo.ListAsync(searchTerm, role, specialty, active, skip, take);
            var total = await _repo.CountAsync(searchTerm, role, specialty, active);
            return new UserPage { Total = total, Items = items };
        }

        public Task UpsertAsync(User u) => _repo.UpsertAsync(u);

        /// <summary>
        /// Reconcilia el espejo local (AppUser/UserRole/UserSpecialty) con lo que acaba de
        /// informar el hospital al loguearse, y devuelve el UserInfo SANEADO (con los códigos
        /// que efectivamente quedaron grabados) para que la cookie/JWT no lleve un código que
        /// la base rechazó.
        ///
        /// Códigos de especialidad que el hospital informa y NO están en el catálogo: se
        /// ignoran y se loguean, nunca se dan de alta solos. Un catálogo nuevo es una
        /// decisión de negocio (nombre, color, orden), no un efecto colateral de un login.
        /// </summary>
        public async Task<UserInfo> SyncFromHospitalAsync(UserInfo info)
        {
            if (info == null) return null;

            IReadOnlyList<string> sanitizedSpecialties = null;
            if (info.Specialties != null)
            {
                var known = new System.Collections.Generic.HashSet<string>(
                    await _catalog.AllCodesAsync(), System.StringComparer.OrdinalIgnoreCase);
                var valid = info.Specialties.Where(c => !string.IsNullOrWhiteSpace(c) && known.Contains(c.Trim())).ToList();
                var ignored = info.Specialties.Except(valid, System.StringComparer.OrdinalIgnoreCase).ToList();
                if (ignored.Count > 0)
                {
                    Trace.TraceWarning("Login " + info.Id + ": el hospital informo especialidades fuera del "
                        + "catalogo y se ignoraron: " + string.Join(",", ignored));
                }
                sanitizedSpecialties = valid;
            }

            // Cada usuario tiene un único rol (ver Domain.Enums.Role): si lo que llega trae
            // varios -o uno heredado/desconocido, como un "Observador" de antes de esta
            // feature- se colapsa al más alto vía RoleHierarchy.Principal. Nunca se persiste
            // más de una fila en UserRole por usuario.
            List<string> normalizedRoles = null;
            if (info.Roles != null)
                normalizedRoles = new List<string> { RoleHierarchy.Principal(info.Roles) };

            await _repo.SyncFromHospitalAsync(info.Id, info.DisplayName, null, normalizedRoles, sanitizedSpecialties);

            return new UserInfo
            {
                Id = info.Id,
                DisplayName = info.DisplayName,
                Roles = normalizedRoles ?? info.Roles,
                Specialties = sanitizedSpecialties?.ToList() ?? info.Specialties
            };
        }

        /// <summary>
        /// Cambia el rol único y global de un usuario (ABM de /Users). No permite dejar el
        /// sistema sin ningún Administrador activo, ni que un admin se quite a sí mismo ese
        /// rol -- ambos son bloqueos de cuenta silenciosos si no se los ataja acá.
        /// </summary>
        /// <param name="actingUserId">Id de quien hace el cambio, para la regla de auto-degradación. Null = no se valida (p. ej. sync automático).</param>
        public async Task SetRoleAsync(string userId, string role, string actingUserId = null)
        {
            if (!Role.IsKnown(role))
                throw new ArgumentException("Rol desconocido: " + role, nameof(role));

            var user = await _repo.GetAsync(userId);
            if (user == null)
                throw new NotFoundException("Usuario inexistente: " + userId);

            var currentRole = RoleHierarchy.Principal(user.Roles?.Select(r => r.Role));
            if (string.Equals(currentRole, Role.Administrador, StringComparison.Ordinal)
                && !string.Equals(role, Role.Administrador, StringComparison.Ordinal))
            {
                if (actingUserId != null && string.Equals(actingUserId, userId, StringComparison.OrdinalIgnoreCase))
                    throw new BusinessRuleException("No podés quitarte a vos mismo el rol Administrador.");

                var otherAdmins = await _repo.CountAsync(null, Role.Administrador, null, true);
                // El propio `user` todavía cuenta como Administrador acá (no se escribió el
                // cambio todavía), así que "otro" admin activo requiere que el conteo sea > 1
                // salvo que este usuario esté inactivo (no cuenta como salvaguarda real).
                if (otherAdmins <= 1 && user.IsActive)
                    throw new BusinessRuleException("Debe existir al menos un Administrador activo en el sistema.");
            }

            await _repo.SyncFromHospitalAsync(userId, user.DisplayName, null, new List<string> { role }, null);
        }
    }

    public class UserPage
    {
        public int Total { get; set; }
        public IReadOnlyList<User> Items { get; set; } = new List<User>();
    }
}
