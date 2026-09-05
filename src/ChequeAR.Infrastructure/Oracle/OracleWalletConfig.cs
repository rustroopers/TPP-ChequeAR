using System;
using System.IO;
using Oracle.ManagedDataAccess.Client;

namespace ChequeAR.Infrastructure.Oracle
{
    /// <summary>
    /// Apunta ODP.NET al wallet de la Autonomous Database.
    ///
    /// Va por código y no por &lt;oracle.manageddataaccess.client&gt; en el Web.config porque
    /// la ruta del wallet cambia entre local (carpeta del usuario) y Azure App Service
    /// (bajo C:\home), y ODP.NET NO expande variables de entorno al leer esa sección.
    /// Seteando <c>WalletLocation</c> además no depende del WALLET_LOCATION del sqlnet.ora,
    /// que el wallet descargado trae con un placeholder (?/network/admin) sin resolver.
    ///
    /// Tiene que ejecutarse antes de la primera conexión: se llama desde Application_Start.
    /// </summary>
    public static class OracleWalletConfig
    {
        /// <summary>
        /// Configura el wallet. Si <paramref name="walletDir"/> viene vacío no hace nada,
        /// para no romper el escenario de desarrollo contra XE local, que usa EZ-Connect
        /// (host:puerto/servicio) y no necesita wallet ni tnsnames.ora.
        /// </summary>
        public static void Configure(string walletDir)
        {
            if (string.IsNullOrWhiteSpace(walletDir)) return;

            walletDir = Environment.ExpandEnvironmentVariables(walletDir.Trim());

            // Falla temprano y con un mensaje que dice la ruta: si el wallet no está donde
            // se lo espera, el sintoma por defecto es un ORA-12154 al primer query, que no
            // da ninguna pista de que el problema es de archivos.
            var tnsnames = Path.Combine(walletDir, "tnsnames.ora");
            if (!File.Exists(tnsnames))
            {
                throw new FileNotFoundException(
                    "No se encontro tnsnames.ora en el wallet configurado en Oracle:WalletDir. " +
                    "Sin eso el alias TNS del connection string no resuelve (ORA-12154). " +
                    "Ruta buscada: " + tnsnames, tnsnames);
            }

            OracleConfiguration.TnsAdmin = walletDir;
            OracleConfiguration.WalletLocation = walletDir;
        }
    }
}
