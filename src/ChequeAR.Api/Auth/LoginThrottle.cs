using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

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

        private static readonly ConcurrentDictionary<string, Bucket> Attempts =
            new ConcurrentDictionary<string, Bucket>(StringComparer.OrdinalIgnoreCase);
        private static long _lastSweepTicks = DateTime.UtcNow.Ticks;

        private sealed class Bucket
        {
            public readonly Queue<DateTime> Failures = new Queue<DateTime>();
            public bool Dead;
        }

        public static bool IsBlocked(string ip)
        {
            var now = DateTime.UtcNow;
            Sweep(now);
            if (!Attempts.TryGetValue(Key(ip), out var bucket)) return false;
            lock (bucket)
            {
                Expire(bucket, now);
                return bucket.Failures.Count >= MaxAttempts;
            }
        }

        public static void RegisterFailure(string ip)
        {
            var now = DateTime.UtcNow;
            while (true)
            {
                var bucket = Attempts.GetOrAdd(Key(ip), _ => new Bucket());
                lock (bucket)
                {
                    if (bucket.Dead) continue;
                    Expire(bucket, now);
                    bucket.Failures.Enqueue(now);
                    return;
                }
            }
        }

        private static string Key(string ip) => string.IsNullOrWhiteSpace(ip) ? "?" : ip.Trim();

        private static void Expire(Bucket bucket, DateTime now)
        {
            while (bucket.Failures.Count > 0 && now - bucket.Failures.Peek() > Window) bucket.Failures.Dequeue();
        }

        // Limpia las IP sin intentos recientes.
        private static void Sweep(DateTime now)
        {
            var last = Interlocked.Read(ref _lastSweepTicks);
            if (now.Ticks - last < Window.Ticks) return;
            if (Interlocked.CompareExchange(ref _lastSweepTicks, now.Ticks, last) != last) return;
            foreach (var pair in Attempts)
            {
                lock (pair.Value)
                {
                    Expire(pair.Value, now);
                    if (pair.Value.Failures.Count > 0) continue;
                    pair.Value.Dead = true;
                    Attempts.TryRemove(pair.Key, out _);
                }
            }
        }
    }
}
