using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace ChequeAR.Api.Auth
{
    /// <summary>
    /// Límite de logins fallidos por IP, en memoria (una sola instancia). Se chequea antes del
    /// PBKDF2. Cuenta sólo fallos porque en el hospital muchos puestos comparten IP pública.
    /// </summary>
    public static class LoginThrottle
    {
        public const int MaxAttempts = 20;
        public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

        private static readonly ConcurrentDictionary<string, Queue<DateTime>> Attempts =
            new ConcurrentDictionary<string, Queue<DateTime>>(StringComparer.OrdinalIgnoreCase);
        private static DateTime _lastSweep = DateTime.UtcNow;

        public static bool IsBlocked(string ip)
        {
            var now = DateTime.UtcNow;
            Sweep(now);
            if (!Attempts.TryGetValue(Key(ip), out var queue)) return false;
            lock (queue)
            {
                while (queue.Count > 0 && now - queue.Peek() > Window) queue.Dequeue();
                return queue.Count >= MaxAttempts;
            }
        }

        public static void RegisterFailure(string ip)
        {
            var now = DateTime.UtcNow;
            var queue = Attempts.GetOrAdd(Key(ip), _ => new Queue<DateTime>());
            lock (queue)
            {
                while (queue.Count > 0 && now - queue.Peek() > Window) queue.Dequeue();
                queue.Enqueue(now);
            }
        }

        private static string Key(string ip) => string.IsNullOrWhiteSpace(ip) ? "?" : ip.Trim();

        // Limpia las IP sin intentos recientes.
        private static void Sweep(DateTime now)
        {
            if (now - _lastSweep < Window) return;
            _lastSweep = now;
            foreach (var pair in Attempts)
            {
                lock (pair.Value)
                {
                    while (pair.Value.Count > 0 && now - pair.Value.Peek() > Window) pair.Value.Dequeue();
                    if (pair.Value.Count == 0) Attempts.TryRemove(pair.Key, out _);
                }
            }
        }
    }
}
