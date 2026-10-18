using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Application.Services
{
    /// <summary>
    /// Carga y cachea el catálogo de especialidades. Singleton (igual que AuditDispatcher):
    /// la fábrica de repositorio crea su propio DbContext en cada recarga y lo descarta —el
    /// service es de vida larga, el DbContext no puede serlo.
    /// </summary>
    public sealed class SpecialtyCatalogService : IDisposable
    {
        private readonly Func<ISpecialtyRepository> _repoFactory;
        private readonly TimeSpan _ttl;
        private readonly TimeSpan _retryTtl = TimeSpan.FromSeconds(30);
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        private volatile SpecialtyCatalog _snapshot;
        private DateTime _loadedAtUtc = DateTime.MinValue;

        public SpecialtyCatalogService(Func<ISpecialtyRepository> repoFactory, int ttlMinutes = 10)
        {
            _repoFactory = repoFactory;
            _ttl = TimeSpan.FromMinutes(ttlMinutes);
        }

        /// <summary>Último snapshot cargado, sin tocar la base. SpecialtyCatalog.Empty si nunca cargó.</summary>
        public SpecialtyCatalog Current => _snapshot ?? SpecialtyCatalog.Empty;

        public async Task<SpecialtyCatalog> GetAsync()
        {
            if (_snapshot != null && DateTime.UtcNow - _loadedAtUtc < _ttl) return _snapshot;

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                // Re-chequear adentro del lock: otro request pudo haber recargado mientras
                // esperábamos el semáforo (evita el stampede de recargas concurrentes).
                if (_snapshot != null && DateTime.UtcNow - _loadedAtUtc < _ttl) return _snapshot;

                try
                {
                    var repo = _repoFactory();
                    IReadOnlyList<Specialty> items;
                    try { items = await repo.ListAsync(includeInactive: true).ConfigureAwait(false); }
                    finally { (repo as IDisposable)?.Dispose(); }
                    _snapshot = new SpecialtyCatalog(items);
                    _loadedAtUtc = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning("No se pudo recargar el catálogo de especialidades; se conserva el anterior. " + ex);
                    // TTL de reintento corto: no martillar la base en cada request mientras
                    // está caída, pero tampoco esperar los 10 minutos completos.
                    _loadedAtUtc = DateTime.UtcNow - _ttl + _retryTtl;
                }
                return Current;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>Fuerza una recarga en el próximo GetAsync (para cuando exista ABM de especialidades).</summary>
        public void Invalidate() => _loadedAtUtc = DateTime.MinValue;

        /// <summary>Todos los códigos (incluye inactivos): para validar el sync on-login contra el hospital.</summary>
        public async Task<System.Collections.Generic.IReadOnlyList<string>> AllCodesAsync()
            => (await GetAsync().ConfigureAwait(false)).AllCodes();

        public async Task<bool> ExistsActiveAsync(string code)
            => (await GetAsync().ConfigureAwait(false)).ExistsActive(code);

        public void Dispose() => _gate.Dispose();
    }
}
