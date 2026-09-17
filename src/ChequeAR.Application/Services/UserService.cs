using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Dtos;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Entities;

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

            await _repo.SyncFromHospitalAsync(info.Id, info.DisplayName, null, info.Roles, sanitizedSpecialties);

            return new UserInfo
            {
                Id = info.Id,
                DisplayName = info.DisplayName,
                Roles = info.Roles,
                Specialties = sanitizedSpecialties?.ToList() ?? info.Specialties
            };
        }
    }

    public class UserPage
    {
        public int Total { get; set; }
        public IReadOnlyList<User> Items { get; set; } = new List<User>();
    }
}
