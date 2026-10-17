using System;
using System.Threading;
using System.Threading.Tasks;

namespace ChequeAR.Application.Services
{
    /// <summary>
    /// Borra los adjuntos que ninguna ficha usa. El renderer sube el archivo apenas se elige y
    /// lo vincula en el siguiente guardado, así que el margen cubre una sesión de carga larga.
    /// </summary>
    public sealed class OrphanAttachmentSweeper : IDisposable
    {
        public static readonly TimeSpan Grace = TimeSpan.FromDays(7);

        private readonly Func<DateTime, Task<int>> _purge;
        private readonly Func<DateTime> _utcNow;
        private readonly Action<Exception> _onError;
        private readonly Action<int> _onPurged;
        private readonly Timer _timer;
        private int _running;

        public OrphanAttachmentSweeper(Func<DateTime, Task<int>> purge, TimeSpan firstRun, TimeSpan period,
            Action<int> onPurged = null, Action<Exception> onError = null, Func<DateTime> utcNow = null)
        {
            _purge = purge;
            _onPurged = onPurged;
            _onError = onError;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            _timer = new Timer(_ => { _ = RunAsync(); }, null, firstRun, period);
        }

        public async Task<int> RunAsync()
        {
            if (Interlocked.Exchange(ref _running, 1) == 1) return 0;
            try
            {
                var borrados = await _purge(_utcNow() - Grace).ConfigureAwait(false);
                if (borrados > 0) _onPurged?.Invoke(borrados);
                return borrados;
            }
            catch (Exception ex)
            {
                _onError?.Invoke(ex);
                return 0;
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }
        }

        public void Dispose() => _timer.Dispose();
    }
}
