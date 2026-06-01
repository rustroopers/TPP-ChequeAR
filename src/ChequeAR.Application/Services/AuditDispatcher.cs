using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Application.Services
{
    public class AuditDispatcher : IDisposable
    {
        private readonly IAuditRepository _repo;
        private readonly ConcurrentQueue<AuditEvent> _queue = new ConcurrentQueue<AuditEvent>();
        private readonly Timer _timer;
        private readonly int _flushIntervalMs;
        private int _disposed;

        public AuditDispatcher(IAuditRepository repo, int flushIntervalMs = 1000)
        {
            _repo = repo;
            _flushIntervalMs = flushIntervalMs;
            _timer = new Timer(_ => FlushAsync().GetAwaiter().GetResult(), null, flushIntervalMs, flushIntervalMs);
        }

        public void Enqueue(AuditEvent evt)
        {
            _queue.Enqueue(evt);
        }

        public async Task PersistImmediatelyAsync(AuditEvent evt)
        {
            await _repo.AddAsync(evt);
        }

        public async Task FlushAsync()
        {
            var batch = new List<AuditEvent>();
            while (_queue.TryDequeue(out var evt))
            {
                batch.Add(evt);
                if (batch.Count >= 500) break;
            }
            if (batch.Count > 0)
            {
                await _repo.AddManyAsync(batch);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            _timer.Dispose();
            FlushAsync().GetAwaiter().GetResult();
        }
    }
}
