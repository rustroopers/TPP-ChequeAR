using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using ChequeAR.Infrastructure.EF;

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
                var passwordHash = existing.PasswordHash;
                var passwordUpdatedAt = existing.PasswordUpdatedAt;
                _ctx.Entry(existing).CurrentValues.SetValues(u);
                existing.PasswordHash = passwordHash;
                existing.PasswordUpdatedAt = passwordUpdatedAt;
            }
            await _ctx.SaveChangesAsync();
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
