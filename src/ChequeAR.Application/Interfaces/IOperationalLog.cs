using System;

namespace ChequeAR.Application.Interfaces
{
    public interface IOperationalLog
    {
        void Warning(Exception ex, string message);
    }

    public sealed class NullOperationalLog : IOperationalLog
    {
        public static readonly NullOperationalLog Instance = new NullOperationalLog();
        public void Warning(Exception ex, string message) { }
    }
}
