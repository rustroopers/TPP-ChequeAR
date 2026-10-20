using System;
using ChequeAR.Application.Interfaces;

namespace ChequeAR.Api.Helpers
{
    public sealed class SerilogOperationalLog : IOperationalLog
    {
        public void Warning(Exception ex, string message)
            => Serilog.Log.Warning(ex, "{OperationalMessage}", message);
    }
}
