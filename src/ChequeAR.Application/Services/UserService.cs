using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Dtos;
using ChequeAR.Application.Exceptions;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Security;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using Newtonsoft.Json;

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
        public const string AuditCategory = "security";
        public const int MinPasswordLength = 10;

        private readonly IUserRepository _repo;
        private readonly SpecialtyCatalogService _catalog;
        private readonly AuditDispatcher _audit;

        public UserService(IUserRepository repo, SpecialtyCatalogService catalog, AuditDispatcher audit = null)
        {
            _repo = repo;
            _catalog = catalog;
            _audit = audit;
        }

        public void LoginSucceeded(string userId) => Audit("execute", userId, "login", userId, "0");

        public async Task LoginFailedAsync(string typed)
        {
            var name = await LoggableUserNameAsync(typed);
            Audit("execute", name, "login-failed", name, "4");
        }

        public void LoggedOut(string userId) => Audit("execute", userId, "logout", userId, "0");

        /// <summary>Null si se cambió; si no, el motivo para la persona.</summary>
        public async Task<string> ChangePasswordAsync(string userId, string current, string replacement, DateTime? nowUtc = null)
        {
            var user = await _repo.GetAsync(userId);
            if (user == null || !user.IsActive || string.IsNullOrEmpty(user.PasswordHash))
                return "No se pudo cambiar la contraseña.";

            if (!PasswordHasher.Verify(current, user.PasswordHash))
            {
                // Cuenta como un login fallido: si no, esta pantalla serviría para probar contraseñas.
                var now = nowUtc ?? DateTime.UtcNow;
                var total = await _repo.IncrementFailedLoginAsync(user.Id);
                var (_, until) = LoginLockoutPolicy.AfterFailure(total - 1, now);
                if (until.HasValue) await _repo.SetLockoutUntilAsync(user.Id, until.Value);
                return "La contraseña actual no es correcta.";
            }

            replacement = replacement ?? string.Empty;
            if (replacement.Length < MinPasswordLength)
                return "La nueva contraseña tiene que tener al menos " + MinPasswordLength + " caracteres.";
            if (replacement == current)
                return "La nueva contraseña tiene que ser distinta de la actual.";
            if (string.Equals(replacement.Trim(), user.Id, StringComparison.OrdinalIgnoreCase))
                return "La contraseña no puede ser igual al usuario.";

            await _repo.SetPasswordHashAsync(user.Id, PasswordHasher.Hash(replacement));
            if (user.FailedLoginCount != 0 || user.LockoutUntil.HasValue)
                await _repo.SetLoginStateAsync(user.Id, 0, null);
            await _repo.RotateSecurityStampAsync(user.Id);
            Audit("update", user.Id, "password", user.Id, "0");
            return null;
        }

        /// <summary>
        /// Edición desde el ABM de usuarios: rol, estado, nombre y especialidades, con un único
        /// evento de auditoría con lo que cambió.
        /// </summary>
        public async Task UpdateByAdminAsync(string userId, string displayName, bool active, string role,
            IReadOnlyList<string> specialties, string actingUserId)
        {
            var before = await _repo.GetAsync(userId);
            if (before == null) throw new NotFoundException("Usuario inexistente: " + userId);
            var antes = Describe(before);

            // Las reglas se chequean antes de escribir: rol primero (con el usuario todavía
            // activo) y después la desactivación.
            if (!active) await EnsureCanDeactivateAsync(userId, actingUserId);
            await SetRoleAsync(userId, role, actingUserId);

            var existing = await _repo.GetAsync(userId);
            existing.DisplayName = displayName ?? existing.DisplayName;
            existing.IsActive = active;
            await UpsertAsync(existing);

            // Lista explícita: null significaría "no tocar", que sirve para el sync del
            // hospital pero no para un ABM, donde se manda el estado completo.
            await SyncFromHospitalAsync(new UserInfo
            {
                Id = existing.Id,
                DisplayName = existing.DisplayName,
                Roles = null,
                Specialties = (specialties ?? new List<string>())
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            });

            AuditChanges(userId, antes, Describe(await _repo.GetAsync(userId)), actingUserId);
        }

        /// <summary>Nombre, correo y estado (API de usuarios).</summary>
        public async Task UpdateBasicsAsync(string userId, string displayName, string email, bool active, string actingUserId)
        {
            var before = await _repo.GetAsync(userId);
            if (!active && before != null) await EnsureCanDeactivateAsync(userId, actingUserId);
            await UpsertAsync(new User
            {
                Id = userId,
                DisplayName = displayName,
                Email = email,
                IsActive = active,
                LastSyncAt = DateTime.UtcNow
            });
            AuditChanges(userId, before == null ? new Dictionary<string, string>() : Describe(before),
                Describe(await _repo.GetAsync(userId)), actingUserId);
        }

        private static Dictionary<string, string> Describe(User u)
        {
            if (u == null) return new Dictionary<string, string>();
            return new Dictionary<string, string>
            {
                ["Nombre"] = u.DisplayName,
                ["Correo"] = u.Email,
                ["Activo"] = u.IsActive ? "Sí" : "No",
                ["Rol"] = RoleHierarchy.Principal(u.Roles?.Select(r => r.Role)),
                ["Especialidades"] = string.Join(", ", (u.Specialties ?? new List<UserSpecialty>())
                    .Select(s => s.SpecialtyCode).OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
            };
        }

        private void AuditChanges(string userId, Dictionary<string, string> antes, Dictionary<string, string> despues, string actingUserId)
        {
            var cambios = AuditChangeSet.Between(antes, despues);
            if (cambios.IsEmpty) return;
            Audit("update", userId, null, actingUserId, "0", cambios);
        }

        private void Audit(string action, string userRef, string operation, string agent, string outcome, AuditChangeSet changes = null)
        {
            if (_audit == null) return;
            var evt = AuditEventBuilder.For(action, "AppUser/" + userRef + (operation == null ? "" : "$" + operation),
                agent, outcome, AuditCategory);
            if (changes != null) evt.FhirJson = changes.ToJson();
            _audit.Enqueue(evt);
        }

        public Task<User> GetAsync(string id) => _repo.GetAsync(id);

        /// <summary>Usuario tipeado en un login fallido, solo si existe (a veces pegan la contraseña ahí).</summary>
        public async Task<string> LoggableUserNameAsync(string typed)
        {
            var t = (typed ?? string.Empty).Trim();
            if (t.Length == 0 || t.Length > 64) return "(inexistente)";
            try
            {
                return await _repo.GetAsync(t) != null ? t : "(inexistente)";
            }
            catch (Exception)
            {
                return "(no verificado)";
            }
        }

        public async Task<UserPage> ListAsync(
            string searchTerm = null, string role = null, string specialty = null,
            bool? active = null, int skip = 0, int take = 50)
        {
            var items = await _repo.ListAsync(searchTerm, role, specialty, active, skip, take);
            var total = await _repo.CountAsync(searchTerm, role, specialty, active);
            return new UserPage { Total = total, Items = items };
        }

        /// <summary>Desactivar a un administrador pasa por el mismo resguardo que degradarlo.</summary>
        public async Task UpsertAsync(User u)
        {
            var actual = u.IsActive ? null : await _repo.GetAsync(u.Id);
            var desactivaAdmin = actual != null && actual.IsActive
                && string.Equals(RoleHierarchy.Principal(actual.Roles?.Select(r => r.Role)), Role.Administrador, StringComparison.Ordinal);
            if (!desactivaAdmin)
            {
                await _repo.UpsertAsync(u);
                return;
            }
            if (!await _repo.KeepingAnAdminAsync(() => _repo.UpsertAsync(u)))
                throw new BusinessRuleException("Debe existir al menos un Administrador activo en el sistema.");
        }

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

            // Dado de baja acá: el hospital puede seguir validando su contraseña.
            var existente = await _repo.GetAsync(info.Id);
            if (existente != null && !existente.IsActive) return null;

            IReadOnlyList<string> sanitizedSpecialties = null;
            if (info.Specialties != null)
            {
                var known = new System.Collections.Generic.HashSet<string>(
                    await _catalog.AllCodesAsync(), System.StringComparer.OrdinalIgnoreCase);
                // Catálogo vacío = falló su carga: filtrar contra él revocaría todas las especialidades.
                if (known.Count == 0)
                {
                    Trace.TraceWarning("Login " + info.Id + ": catalogo de especialidades vacio, no se tocan las del usuario.");
                }
                else
                {
                    var valid = info.Specialties.Where(c => !string.IsNullOrWhiteSpace(c) && known.Contains(c.Trim())).ToList();
                    var ignored = info.Specialties.Except(valid, System.StringComparer.OrdinalIgnoreCase).ToList();
                    if (ignored.Count > 0)
                    {
                        Trace.TraceWarning("Login " + info.Id + ": el hospital informo especialidades fuera del "
                            + "catalogo y se ignoraron: " + string.Join(",", ignored));
                    }
                    sanitizedSpecialties = valid;
                }
            }

            // Cada usuario tiene un único rol (ver Domain.Enums.Role): si lo que llega trae
            // varios -o uno heredado/desconocido, como un "Observador" de antes de esta
            // feature- se colapsa al más alto vía RoleHierarchy.Principal. Nunca se persiste
            // más de una fila en UserRole por usuario.
            List<string> normalizedRoles = null;
            if (info.Roles != null)
                normalizedRoles = new List<string> { RoleHierarchy.Principal(info.Roles) };

            await _repo.SyncFromHospitalAsync(info.Id, info.DisplayName, null, normalizedRoles, sanitizedSpecialties);

            var stamp = await _repo.EnsureSecurityStampAsync(info.Id);

            return new UserInfo
            {
                Id = info.Id,
                DisplayName = info.DisplayName,
                Roles = normalizedRoles ?? info.Roles,
                Specialties = sanitizedSpecialties?.ToList() ?? info.Specialties,
                SecurityStamp = stamp
            };
        }

        public Task RotateSecurityStampAsync(string userId) => _repo.RotateSecurityStampAsync(userId);

        /// <summary>
        /// Cambia el rol único y global de un usuario (ABM de /Users). No permite dejar el
        /// sistema sin ningún Administrador activo, ni que un admin se quite a sí mismo ese
        /// rol -- ambos son bloqueos de cuenta silenciosos si no se los ataja acá.
        /// </summary>
        /// <param name="actingUserId">Id de quien hace el cambio, para la regla de auto-degradación. Null = no se valida (p. ej. sync automático).</param>
        /// <summary>
        /// Se chequea antes de escribir nada: si no, la desactivación quedaba guardada aunque
        /// después fallara el cambio de rol.
        /// </summary>
        public async Task EnsureCanDeactivateAsync(string userId, string actingUserId)
        {
            var user = await _repo.GetAsync(userId);
            if (user == null || !user.IsActive) return;
            if (actingUserId != null && string.Equals(actingUserId, userId, StringComparison.OrdinalIgnoreCase))
                throw new BusinessRuleException("No puede desactivar su propio usuario.");
            var rol = RoleHierarchy.Principal(user.Roles?.Select(r => r.Role));
            if (string.Equals(rol, Role.Administrador, StringComparison.Ordinal)
                && await _repo.CountAsync(null, Role.Administrador, null, true) <= 1)
                throw new BusinessRuleException("Debe existir al menos un Administrador activo en el sistema.");
        }

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
                    throw new BusinessRuleException("No puede quitarse a sí mismo el rol Administrador.");

                var otherAdmins = await _repo.CountAsync(null, Role.Administrador, null, true);
                // El propio `user` todavía cuenta como Administrador acá (no se escribió el
                // cambio todavía), así que "otro" admin activo requiere que el conteo sea > 1
                // salvo que este usuario esté inactivo (no cuenta como salvaguarda real).
                if (otherAdmins <= 1 && user.IsActive)
                    throw new BusinessRuleException("Debe existir al menos un Administrador activo en el sistema.");
            }

            var esAdmin = string.Equals(currentRole, Role.Administrador, StringComparison.Ordinal);
            if (!esAdmin || string.Equals(role, Role.Administrador, StringComparison.Ordinal))
            {
                await _repo.SyncFromHospitalAsync(userId, user.DisplayName, null, new List<string> { role }, null);
                return;
            }
            // El chequeo de arriba evita el viaje en el caso común; éste ataja dos
            // degradaciones simultáneas que lo pasaron las dos.
            if (!await _repo.KeepingAnAdminAsync(() => _repo.SyncFromHospitalAsync(userId, user.DisplayName, null, new List<string> { role }, null)))
                throw new BusinessRuleException("Debe existir al menos un Administrador activo en el sistema.");
        }
    }

    public class UserPage
    {
        public int Total { get; set; }
        public IReadOnlyList<User> Items { get; set; } = new List<User>();
    }
}
