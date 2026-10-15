using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Application.Security
{
    /// <summary>
    /// Revalida una sesión ya emitida (cookie o JWT): usuario activo, mismo sello, rol y
    /// especialidades que en la base. Cachea por usuario para no ir a Oracle en cada request.
    /// Si la base falla usa el último dato conocido, y sin ninguno acepta la sesión: un corte
    /// breve no debería echar a todos con fichas a medio cargar.
    /// </summary>
    public sealed class SessionValidator
    {
        private sealed class Snapshot
        {
            public bool Exists;
            public bool IsActive;
            public string Stamp;
            public string Role;
            public HashSet<string> Specialties;
            public DateTime ExpiresAt;
        }

        private readonly Func<string, Task<User>> _loadUser;
        private readonly TimeSpan _ttl;
        private readonly Action<Exception, string> _onError;
        private readonly Func<DateTime> _utcNow;
        private readonly ConcurrentDictionary<string, Snapshot> _cache =
            new ConcurrentDictionary<string, Snapshot>(StringComparer.OrdinalIgnoreCase);

        public SessionValidator(Func<string, Task<User>> loadUser, TimeSpan ttl,
            Action<Exception, string> onError = null, Func<DateTime> utcNow = null)
        {
            _loadUser = loadUser ?? throw new ArgumentNullException(nameof(loadUser));
            _ttl = ttl;
            _onError = onError ?? ((e, m) => { });
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public async Task<bool> IsValidAsync(string userId, string stamp, string principalRole, IEnumerable<string> specialties)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(stamp)) return false;

            var snapshot = await GetSnapshotAsync(userId).ConfigureAwait(false);
            if (snapshot == null) return true; // base caída y sin dato previo: ver resumen de la clase.

            if (!snapshot.Exists || !snapshot.IsActive) return false;
            if (!string.Equals(snapshot.Stamp, stamp, StringComparison.Ordinal)) return false;
            if (!string.Equals(snapshot.Role, principalRole, StringComparison.Ordinal)) return false;

            var actuales = new HashSet<string>(
                (specialties ?? Enumerable.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)),
                StringComparer.OrdinalIgnoreCase);
            return actuales.SetEquals(snapshot.Specialties);
        }

        public void Invalidate(string userId)
        {
            if (!string.IsNullOrEmpty(userId)) _cache.TryRemove(userId, out _);
        }

        private async Task<Snapshot> GetSnapshotAsync(string userId)
        {
            var now = _utcNow();
            _cache.TryGetValue(userId, out var cached);
            if (cached != null && cached.ExpiresAt > now) return cached;

            try
            {
                var user = await _loadUser(userId).ConfigureAwait(false);
                var fresh = user == null
                    ? new Snapshot { Exists = false, Specialties = new HashSet<string>() }
                    : new Snapshot
                    {
                        Exists = true,
                        IsActive = user.IsActive,
                        Stamp = user.SecurityStamp,
                        Role = RoleHierarchy.Principal((user.Roles ?? new List<UserRole>()).Select(r => r.Role)),
                        Specialties = new HashSet<string>(
                            (user.Specialties ?? new List<UserSpecialty>()).Select(s => s.SpecialtyCode),
                            StringComparer.OrdinalIgnoreCase)
                    };
                fresh.ExpiresAt = now.Add(_ttl);
                _cache[userId] = fresh;
                return fresh;
            }
            catch (Exception ex)
            {
                _onError(ex, "No se pudo revalidar la sesion de " + userId + " contra la base; se usa el ultimo dato conocido.");
                return cached;
            }
        }
    }
}
