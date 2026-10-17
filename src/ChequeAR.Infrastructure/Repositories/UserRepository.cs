using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using ChequeAR.Infrastructure.EF;
using Oracle.ManagedDataAccess.Client;

namespace ChequeAR.Infrastructure.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly ChequeARDbContext _ctx;
        public UserRepository(ChequeARDbContext ctx) { _ctx = ctx; }

        public Task<User> GetAsync(string id) =>
            _ctx.Users.Include(u => u.Roles).Include(u => u.Specialties).FirstOrDefaultAsync(u => u.Id == id);

        public async Task UpsertAsync(User u)
        {
            var existing = await _ctx.Users.FindAsync(u.Id);
            if (existing == null)
            {
                _ctx.Users.Add(u);
            }
            else
            {
                // PasswordHash/PasswordUpdatedAt se preservan SIEMPRE, pase lo que pase en
                // `u`: este método es el ABM administrativo genérico (UsersController.Save,
                // el PUT de UserController), nunca el camino de cambiar una contraseña. Sin
                // esto, CurrentValues.SetValues pisaría el hash con lo que sea que traiga
                // `u.PasswordHash` (típicamente null, porque nadie arma ese campo a mano) y
                // el usuario quedaría sin poder loguearse por haber editado, p. ej., su email.
                // Por lo mismo, solo los campos editables: SetValues también pisaría LastSyncAt,
                // LastLoginAt, el sello y el bloqueo.
                var desactivado = existing.IsActive && !u.IsActive;
                existing.DisplayName = u.DisplayName;
                existing.Email = u.Email;
                existing.IsActive = u.IsActive;
                if (desactivado) await RotateAsync(existing);
            }
            await _ctx.SaveChangesAsync();
        }

        private static string NewStamp() => Guid.NewGuid().ToString("N");

        /// <summary>Nuevo sello y refresh tokens revocados; se guarda con el SaveChanges del llamador.</summary>
        private async Task RotateAsync(User user)
        {
            user.SecurityStamp = NewStamp();
            var now = DateTime.UtcNow;
            var vigentes = await _ctx.RefreshTokens
                .Where(t => t.UserId == user.Id && t.RevokedAt == null && t.ExpiresAt > now)
                .ToListAsync();
            foreach (var t in vigentes) t.RevokedAt = now;
        }

        public async Task<string> EnsureSecurityStampAsync(string userId)
        {
            var user = await _ctx.Users.FindAsync(userId);
            if (user == null) return null;
            if (string.IsNullOrEmpty(user.SecurityStamp))
            {
                user.SecurityStamp = NewStamp();
                await _ctx.SaveChangesAsync();
            }
            return user.SecurityStamp;
        }

        public async Task RotateSecurityStampAsync(string userId)
        {
            var user = await _ctx.Users.FindAsync(userId);
            if (user == null) return;
            await RotateAsync(user);
            await _ctx.SaveChangesAsync();
        }

        public async Task SetLoginStateAsync(string userId, int failedLoginCount, DateTime? lockoutUntil)
        {
            var user = await _ctx.Users.FindAsync(userId);
            if (user == null) return;
            user.FailedLoginCount = failedLoginCount;
            user.LockoutUntil = lockoutUntil;
            await _ctx.SaveChangesAsync();
        }

        public async Task<int> IncrementFailedLoginAsync(string userId)
        {
            await _ctx.Database.ExecuteSqlCommandAsync(
                "UPDATE \"CHEQUEAR\".\"AppUser\" SET \"FailedLoginCount\" = \"FailedLoginCount\" + 1 WHERE \"Id\" = :id",
                new OracleParameter("id", userId));
            var total = await _ctx.Database.SqlQuery<decimal>(
                "SELECT \"FailedLoginCount\" FROM \"CHEQUEAR\".\"AppUser\" WHERE \"Id\" = :id",
                new OracleParameter("id", userId)).FirstOrDefaultAsync();
            return (int)total;
        }

        public async Task SetLockoutUntilAsync(string userId, DateTime lockoutUntil)
        {
            await _ctx.Database.ExecuteSqlCommandAsync(
                "UPDATE \"CHEQUEAR\".\"AppUser\" SET \"LockoutUntil\" = :hasta WHERE \"Id\" = :id",
                new OracleParameter("hasta", lockoutUntil), new OracleParameter("id", userId));
        }

        public async Task SetPasswordHashAsync(string userId, string passwordHash)
        {
            var user = await _ctx.Users.FindAsync(userId);
            if (user == null) return;
            user.PasswordHash = passwordHash;
            user.PasswordUpdatedAt = DateTime.UtcNow;
            await _ctx.SaveChangesAsync();
        }

        private const string ActiveAdmins =
            @"FROM ""CHEQUEAR"".""UserRole"" r JOIN ""CHEQUEAR"".""AppUser"" u ON u.""Id"" = r.""UserId"" WHERE r.""Role"" = 'Administrador' AND u.""IsActive"" = 1";

        public async Task<bool> KeepingAnAdminAsync(Func<Task> change)
        {
            using (var tx = _ctx.Database.BeginTransaction())
            {
                await _ctx.Database.SqlQuery<string>(@"SELECT r.""UserId"" " + ActiveAdmins + " FOR UPDATE").ToListAsync();
                await change();
                var quedan = await _ctx.Database.SqlQuery<decimal>("SELECT COUNT(*) " + ActiveAdmins).SingleAsync();
                if (quedan == 0)
                {
                    tx.Rollback();
                    foreach (var entry in _ctx.ChangeTracker.Entries().ToList()) entry.State = EntityState.Detached;
                    return false;
                }
                tx.Commit();
                return true;
            }
        }

        public async Task SyncFromHospitalAsync(string userId, string displayName, string email,
            IReadOnlyList<string> roles, IReadOnlyList<string> specialties)
        {
            var now = DateTime.UtcNow;
            var user = await _ctx.Users.Include(u => u.Roles).Include(u => u.Specialties)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
            {
                user = new User
                {
                    Id = userId,
                    DisplayName = displayName,
                    Email = email,
                    IsActive = true,
                    LastLoginAt = now,
                    LastSyncAt = now
                };
                _ctx.Users.Add(user);
            }
            else
            {
                user.DisplayName = displayName ?? user.DisplayName;
                user.Email = email ?? user.Email;
                user.LastLoginAt = now;
                user.LastSyncAt = now;
            }

            var diff = UserMirrorDiff.Compute(
                user.Roles.Select(r => r.Role), roles,
                user.Specialties.Select(s => s.SpecialtyCode), specialties);

            // Se opera contra los DbSet, no contra las colecciones de navegación: mismo
            // motivo que QuestionnaireRepository.ReplaceStructureAsync -- las FKs son
            // requeridas, así que sacar un elemento de la colección sin borrarlo de su set
            // intentaría poner la FK en null y EF lo rechazaría.
            foreach (var role in diff.RolesToRemove)
            {
                var row = user.Roles.FirstOrDefault(r => string.Equals(r.Role, role, StringComparison.OrdinalIgnoreCase));
                if (row != null) { _ctx.UserRoles.Remove(row); user.Roles.Remove(row); }
            }
            foreach (var role in diff.RolesToAdd)
            {
                var row = new UserRole { UserId = userId, Role = role };
                _ctx.UserRoles.Add(row);
                user.Roles.Add(row);
            }

            foreach (var code in diff.SpecialtiesToRemove)
            {
                var row = user.Specialties.FirstOrDefault(s => string.Equals(s.SpecialtyCode, code, StringComparison.OrdinalIgnoreCase));
                if (row != null) { _ctx.UserSpecialties.Remove(row); user.Specialties.Remove(row); }
            }
            foreach (var code in diff.SpecialtiesToAdd)
            {
                var row = new UserSpecialty { UserId = userId, SpecialtyCode = code };
                _ctx.UserSpecialties.Add(row);
                user.Specialties.Add(row);
            }

            await _ctx.SaveChangesAsync();
        }

        public async Task<IReadOnlyList<User>> ListAsync(string searchTerm, string roleFilter, string specialtyFilter, bool? active, int skip, int take)
        {
            var q = _ctx.Users.Include(u => u.Roles).Include(u => u.Specialties).AsQueryable();
            if (!string.IsNullOrEmpty(searchTerm))
            {
                q = q.Where(u => u.DisplayName.Contains(searchTerm) || u.Id.Contains(searchTerm) || u.Email.Contains(searchTerm));
            }
            if (!string.IsNullOrEmpty(roleFilter))
            {
                q = q.Where(u => u.Roles.Any(r => r.Role == roleFilter));
            }
            if (!string.IsNullOrEmpty(specialtyFilter))
            {
                q = q.Where(u => u.Specialties.Any(s => s.SpecialtyCode == specialtyFilter));
            }
            if (active.HasValue)
            {
                q = q.Where(u => u.IsActive == active.Value);
            }
            return await q.OrderBy(u => u.DisplayName).Skip(skip).Take(take).ToListAsync();
        }

        public Task<int> CountAsync(string searchTerm, string roleFilter, string specialtyFilter, bool? active)
        {
            var q = _ctx.Users.AsQueryable();
            if (!string.IsNullOrEmpty(searchTerm))
                q = q.Where(u => u.DisplayName.Contains(searchTerm) || u.Id.Contains(searchTerm) || u.Email.Contains(searchTerm));
            if (!string.IsNullOrEmpty(roleFilter))
                q = q.Where(u => u.Roles.Any(r => r.Role == roleFilter));
            if (!string.IsNullOrEmpty(specialtyFilter))
                q = q.Where(u => u.Specialties.Any(s => s.SpecialtyCode == specialtyFilter));
            if (active.HasValue)
                q = q.Where(u => u.IsActive == active.Value);
            return q.CountAsync();
        }
    }
}
