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
            var root = Environment.ExpandEnvironmentVariables(
                ConfigurationManager.AppSettings["Logging:Root"] ?? @"C:\ChequeAR\Logs");

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .Enrich.FromLogContext()
                .WriteTo.File(
                    Path.Combine(root, "chequear-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 31,
                    shared: true)
                // Espeja todo en la ventana Output de Visual Studio (y en cualquier debugger
                // enganchado). Es lo que hace visible el log mientras se depura: el sink de
                // archivo escribe en Logging:Root y nada aparece en la consola de IIS Express.
                // Sin debugger enganchado el sink es un no-op, asi que no cuesta nada dejarlo.
                .WriteTo.Debug()
                .CreateLogger();
        }
    }
}
