using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Application.Services
{
    /// <summary>
    /// Persiste los eventos de auditoría: en lote por timer (<see cref="Enqueue"/>) o en el
    /// acto (<see cref="PersistImmediatelyAsync"/>).
    ///
    /// Vive como singleton, así que no puede retener un repositorio (y con él un DbContext,
    /// que no es thread-safe): pide uno nuevo a la factory en cada operación y lo libera al
    /// terminar. Un fallo de la base nunca sale del callback del timer —en .NET Framework
    /// una excepción no manejada ahí termina el proceso—: se reporta por <c>onError</c> y el
    /// lote vuelve a la cola hasta <see cref="MaxAttempts"/> intentos, espaciados cada vez más.
    /// </summary>
    public class AuditDispatcher : IDisposable
    {
        /// <summary>Intentos de persistir un evento antes de descartarlo (reportándolo por onError).</summary>
        public const int MaxAttempts = 10;
        private const int BatchSize = 500;
        /// <summary>Fallos seguidos, sin ningún acierto, que alcanzan para dar la base por caída.</summary>
        private const int OutageProbe = 3;
        /// <summary>
        /// Espera máxima entre flushes fallidos. Con el timer fijo de 1 s los intentos se
        /// agotaban en segundos; con espera creciente, MaxAttempts cubre varios minutos de caída.
        /// </summary>
        private const int MaxBackoffSeconds = 60;

        private readonly Func<IAuditRepository> _repoFactory;
        private readonly bool _ownsRepositories;
        private readonly Action<Exception, string> _onError;
        private readonly ConcurrentQueue<Pending> _queue = new ConcurrentQueue<Pending>();
        private readonly SemaphoreSlim _flushGate = new SemaphoreSlim(1, 1);
        private readonly Timer _timer;
        private int _disposed;
        // Sólo los toca el callback del timer, que nunca corre en paralelo consigo mismo.
        private int _failedFlushes;
        private DateTime _nextTimerFlushUtc = DateTime.MinValue;

        /// <param name="repoFactory">Crea un repositorio nuevo por operación. Si es IDisposable, se libera al terminar.</param>
        /// <param name="onError">Recibe los fallos de persistencia (no debe lanzar).</param>
        public AuditDispatcher(Func<IAuditRepository> repoFactory, int flushIntervalMs = 1000, Action<Exception, string> onError = null)
            : this(repoFactory, true, flushIntervalMs, onError)
        {
        }

        /// <summary>Repositorio compartido: sólo para tests, donde el repo es un doble sin estado. No se libera.</summary>
        public AuditDispatcher(IAuditRepository repo, int flushIntervalMs = 1000)
            : this(() => repo, false, flushIntervalMs, null)
        {
        }

        private AuditDispatcher(Func<IAuditRepository> repoFactory, bool ownsRepositories, int flushIntervalMs, Action<Exception, string> onError)
        {
            _repoFactory = repoFactory ?? throw new ArgumentNullException(nameof(repoFactory));
            _ownsRepositories = ownsRepositories;
            _onError = onError ?? ((_, __) => { });
            _timer = new Timer(_ => OnTimer(), null, flushIntervalMs, flushIntervalMs);
        }

        /// <summary>IP de quien origina el evento; null fuera de un pedido.</summary>
        public Func<string> AgentIpProvider { get; set; }

        public void Enqueue(AuditEvent evt)
        {
            StampIp(evt);
            _queue.Enqueue(new Pending(evt));
        }

        private void StampIp(AuditEvent evt)
        {
            if (evt == null || evt.AgentIp != null || AgentIpProvider == null) return;
            try { evt.AgentIp = AgentIpProvider(); }
            catch (Exception) { /* sin contexto de pedido */ }
        }

        /// <summary>
        /// Persiste en el acto. Si la base falla, el evento no se pierde: queda en la cola del
        /// flush y el error se reporta, pero no se propaga — la operación de negocio que lo
        /// originó ya se guardó y fallarla por la auditoría confundiría al usuario.
        /// </summary>
        public async Task PersistImmediatelyAsync(AuditEvent evt)
        {
            StampIp(evt);
            try
            {
                await WithRepositoryAsync(r => r.AddAsync(evt));
            }
            catch (Exception ex)
            {
                _onError(ex, "No se pudo persistir el evento de auditoría " + evt?.EntityRef + "; queda encolado para reintentar.");
                _queue.Enqueue(new Pending(evt, attempts: 1));
            }
        }

        /// <summary>Persiste un lote de la cola. Nunca corre en paralelo consigo mismo.</summary>
        public async Task FlushAsync()
        {
            await _flushGate.WaitAsync();
            try
            {
                await FlushBatchAsync();
            }
            finally
            {
                _flushGate.Release();
            }
        }

        /// <returns>false si el lote falló (los eventos volvieron a la cola o se descartaron).</returns>
        private async Task<bool> FlushBatchAsync()
        {
            var batch = new List<Pending>();
            while (batch.Count < BatchSize && _queue.TryDequeue(out var p)) batch.Add(p);
            if (batch.Count == 0) return true;

            try
            {
                var events = new List<AuditEvent>(batch.Count);
                foreach (var p in batch) events.Add(p.Event);
                await WithRepositoryAsync(r => r.AddManyAsync(events));
                return true;
            }
            catch (Exception ex)
            {
                var dropped = batch.Count == 1
                    ? Requeue(batch, ex)
                    : await RetryOneByOneAsync(batch, ex);
                _onError(ex, "Falló el flush de auditoría (" + batch.Count + " eventos, " + dropped + " descartados).");
                return false;
            }
        }

        /// <summary>
        /// El lote se guarda en una sola transacción, así que un único evento inválido lo
        /// tumba entero. Se reintenta de a uno para aislarlo: si alguno entra, la base está
        /// viva y sólo los que fallaron gastan un intento. Si no entra ninguno es una caída,
        /// y se corta enseguida en vez de esperar un timeout por evento.
        /// </summary>
        /// <returns>Cantidad de eventos descartados.</returns>
        private async Task<int> RetryOneByOneAsync(List<Pending> batch, Exception batchError)
        {
            var failed = new List<Pending>();
            Exception lastError = batchError;
            var anySaved = false;

            for (var i = 0; i < batch.Count; i++)
            {
                if (!anySaved && failed.Count >= OutageProbe)
                {
                    failed.AddRange(batch.GetRange(i, batch.Count - i));
                    break;
                }
                var p = batch[i];
                try
                {
                    await WithRepositoryAsync(r => r.AddAsync(p.Event));
                    anySaved = true;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    failed.Add(p);
                }
            }
            return Requeue(failed, lastError);
        }

        /// <summary>Devuelve los eventos a la cola con un intento más, o los descarta si ya agotaron los suyos.</summary>
        private int Requeue(List<Pending> failed, Exception ex)
        {
            var dropped = 0;
            foreach (var p in failed)
            {
                p.Attempts++;
                if (p.Attempts < MaxAttempts) _queue.Enqueue(p);
                else
                {
                    dropped++;
                    _onError(ex, "Evento de auditoría descartado tras " + MaxAttempts + " intentos: "
                                 + p.Event?.Action + " " + p.Event?.EntityRef + " por " + p.Event?.AgentUserId
                                 + " (" + p.Event?.Recorded.ToString("o", CultureInfo.InvariantCulture) + ")");
                }
            }
            return dropped;
        }

        private async Task WithRepositoryAsync(Func<IAuditRepository, Task> action)
        {
            var repo = _repoFactory();
            try
            {
                await action(repo);
            }
            finally
            {
                if (_ownsRepositories) (repo as IDisposable)?.Dispose();
            }
        }

        private void OnTimer()
        {
            // Si el flush anterior sigue corriendo (base lenta), este tick se saltea: no se
            // acumulan hilos esperando el semáforo.
            if (!_flushGate.Wait(0)) return;
            try
            {
                if (DateTime.UtcNow < _nextTimerFlushUtc) return;
                if (FlushBatchAsync().GetAwaiter().GetResult())
                {
                    _failedFlushes = 0;
                }
                else
                {
                    _failedFlushes++;
                    var espera = Math.Min(MaxBackoffSeconds, Math.Pow(2, Math.Min(_failedFlushes, 10)));
                    _nextTimerFlushUtc = DateTime.UtcNow.AddSeconds(espera);
                }
            }
            catch (Exception ex)
            {
                // Red de contención: FlushBatchAsync ya captura los fallos de la base, pero
                // nada puede escapar de un callback de Timer sin terminar el proceso.
                _onError(ex, "Error inesperado en el timer de auditoría.");
            }
            finally
            {
                _flushGate.Release();
            }
        }

        /// <summary>Detiene el timer y vacía la cola (se llama al apagar la aplicación).</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            using (var stopped = new ManualResetEvent(false))
            {
                // Dispose(WaitHandle) espera a que termine un callback en curso.
                if (_timer.Dispose(stopped)) stopped.WaitOne(TimeSpan.FromSeconds(10));
            }

            _flushGate.Wait();
            try
            {
                // Un lote fallido no se reintenta en loop: si la base está caída al apagar,
                // los eventos que queden se reportan como perdidos.
                while (!_queue.IsEmpty && FlushBatchAsync().GetAwaiter().GetResult()) { }
                var lost = _queue.Count;
                if (lost > 0)
                    _onError(null, lost + " eventos de auditoría sin persistir al apagar la aplicación.");
            }
            catch (Exception ex)
            {
                _onError(ex, "Error al vaciar la cola de auditoría al apagar la aplicación.");
            }
            finally
            {
                _flushGate.Release();
            }
            _flushGate.Dispose();
            GC.SuppressFinalize(this);
        }

        private sealed class Pending
        {
            public Pending(AuditEvent evt, int attempts = 0)
            {
                Event = evt;
                Attempts = attempts;
            }

            public AuditEvent Event { get; }
            public int Attempts { get; set; }
        }
    }
}
