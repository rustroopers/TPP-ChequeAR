using System;

namespace ChequeAR.Application.Interfaces
{
    /// <summary>La hora actual, inyectable para probar vencimientos sin esperar.</summary>
    public interface IClock
    {
        DateTime UtcNow { get; }
    }

    public sealed class SystemClock : IClock
    {
        public static readonly SystemClock Instance = new SystemClock();
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
