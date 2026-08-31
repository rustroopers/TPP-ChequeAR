using System;
using System.Configuration;
using System.IO;
using Serilog;

namespace ChequeAR.Api.App_Start
{
    public static class LoggingConfig
    {
        public static void Configure()
        {
            var root = ConfigurationManager.AppSettings["Logging:Root"] ?? @"C:\ChequeAR\Logs";

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .Enrich.FromLogContext()
                .WriteTo.File(
                    Path.Combine(root, "chequear-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 31,
                    shared: true)
                .CreateLogger();
        }
    }
}
