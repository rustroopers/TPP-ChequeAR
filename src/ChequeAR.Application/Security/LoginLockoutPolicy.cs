using System;

namespace ChequeAR.Application.Security
{
    /// <summary>
    /// Cada <see cref="Threshold"/> fallos seguidos la cuenta se bloquea: 5 minutos la primera
    /// vez y el doble cada vez, hasta <see cref="MaxLockout"/>.
    /// </summary>
    public static class LoginLockoutPolicy
    {
        public const int Threshold = 5;
        public static readonly TimeSpan BaseLockout = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan MaxLockout = TimeSpan.FromMinutes(60);

        public static bool IsLockedOut(DateTime? lockoutUntil, DateTime nowUtc)
            => lockoutUntil.HasValue && lockoutUntil.Value > nowUtc;

        public static (int FailedCount, DateTime? LockoutUntil) AfterFailure(int failedCount, DateTime nowUtc)
        {
            var count = Math.Max(0, failedCount) + 1;
            if (count % Threshold != 0) return (count, null);

            // 5 -> 5 min, 10 -> 10 min, 15 -> 20 min, 20 -> 40 min, 25+ -> 60 min.
            var rondas = count / Threshold - 1;
            var minutos = BaseLockout.TotalMinutes * Math.Pow(2, Math.Min(rondas, 10));
            var bloqueo = TimeSpan.FromMinutes(Math.Min(minutos, MaxLockout.TotalMinutes));
            return (count, nowUtc.Add(bloqueo));
        }
    }
}
