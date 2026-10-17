using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Hosting;
using System.Web.Http;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Services;
using Serilog;

namespace ChequeAR.Api.Controllers.Api
{
    /// <summary>
    /// Paso del deploy: export del esquema y migraciones pendientes. Corre dentro de la app
    /// porque es la que tiene el wallet y la configuración de Azure, y en segundo plano porque
    /// el export solo ya tarda minutos (Azure corta los requests a los 230 s). El deploy lo
    /// lanza con POST y consulta con GET hasta que termina.
    /// </summary>
    [RoutePrefix("api/deploy")]
    public class DeployController : ApiController
    {
        private static readonly object Gate = new object();
        private static MigrationStatus _status = new MigrationStatus { State = "idle" };

        private readonly ISchemaMigrator _migrator;

        public DeployController(ISchemaMigrator migrator)
        {
            _migrator = migrator;
        }

        [HttpPost, Route("migrate"), AllowAnonymous]
        public IHttpActionResult Start()
        {
            if (!Authorized()) return NotFound();
            lock (Gate)
            {
                if (_status.State == "running") return Content(HttpStatusCode.Accepted, _status);
                var pending = _migrator.Pending();
                if (pending.Count == 0)
                {
                    _status = new MigrationStatus { State = "done", Applied = new List<string>() };
                    return Ok(_status);
                }
                _status = new MigrationStatus { State = "running", Step = "backup", Pending = pending.ToList() };
            }
            HostingEnvironment.QueueBackgroundWorkItem(_ => Run());
            return Content(HttpStatusCode.Accepted, _status);
        }

        [HttpGet, Route("migrate"), AllowAnonymous]
        public IHttpActionResult Status()
        {
            if (!Authorized()) return NotFound();
            return Ok(_status);
        }

        private void Run()
        {
            try
            {
                var migrator = _migrator;
                var archivo = "chequear_" + DateTime.UtcNow.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
                var backup = migrator.Backup(archivo);
                Log.Information("Deploy: export del esquema en DATA_PUMP_DIR/{Backup}", backup);
                lock (Gate) { _status.Backup = backup; _status.Step = "copy"; }
                CopyAndPrune(migrator, archivo,
                    ConfigurationManager.AppSettings["Deploy:BackupObjectUri"],
                    ConfigurationManager.AppSettings["Deploy:BackupCredential"],
                    int.TryParse(ConfigurationManager.AppSettings["Deploy:BackupsToKeep"], out var n) ? n : BackupRetention.DefaultKeep,
                    _status);
                lock (Gate) { _status.Step = "migrate"; }

                var applied = migrator.Apply();
                Log.Information("Deploy: migraciones aplicadas {Applied}", applied);
                lock (Gate) { _status.State = "done"; _status.Step = null; _status.Applied = applied.ToList(); }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Deploy: falló el paso {Step}", _status.Step);
                lock (Gate) { _status.State = "failed"; _status.Error = ex.GetBaseException().Message; }
            }
        }

        /// <summary>
        /// Copia el export a Object Storage (si está configurado) y borra los viejos. Ninguna de
        /// las dos frena el deploy: el export ya quedó en la base.
        /// </summary>
        public static void CopyAndPrune(ISchemaMigrator migrator, string archivo, string objectUri, string credential,
            int keep, MigrationStatus status)
        {
            if (!string.IsNullOrWhiteSpace(objectUri) && !string.IsNullOrWhiteSpace(credential))
            {
                try
                {
                    migrator.CopyBackup(archivo + ".dmp", objectUri, credential);
                    lock (Gate) status.Copied = true;
                }
                catch (Exception ex)
                {
                    Warn(status, "No se pudo copiar el export a Object Storage", ex);
                }
            }

            try
            {
                var borrar = BackupRetention.ToDelete(migrator.ListBackupFiles(), keep);
                foreach (var f in borrar) migrator.DeleteBackupFile(f);
                lock (Gate) status.Pruned = borrar.ToList();
            }
            catch (Exception ex)
            {
                Warn(status, "No se pudieron borrar los exports viejos", ex);
            }
        }

        private static void Warn(MigrationStatus status, string message, Exception ex)
        {
            Log.Warning(ex, "Deploy: {Message}", message);
            lock (Gate) status.Warnings.Add(message + ": " + ex.GetBaseException().Message);
        }

        // Sin clave configurada el endpoint no existe.
        private bool Authorized()
        {
            var esperada = ConfigurationManager.AppSettings["Deploy:Key"];
            if (string.IsNullOrEmpty(esperada)) return false;
            var recibida = Request.Headers.TryGetValues("X-Deploy-Key", out var valores) ? valores.FirstOrDefault() : null;
            if (string.IsNullOrEmpty(recibida)) return false;
            using (var sha = SHA256.Create())
            {
                var a = sha.ComputeHash(Encoding.UTF8.GetBytes(esperada));
                var b = sha.ComputeHash(Encoding.UTF8.GetBytes(recibida));
                var diff = 0;
                for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
                return diff == 0;
            }
        }

        public class MigrationStatus
        {
            public string State { get; set; }
            public string Step { get; set; }
            public List<string> Pending { get; set; }
            public List<string> Applied { get; set; }
            public string Backup { get; set; }
            public bool Copied { get; set; }
            public List<string> Pruned { get; set; }
            public List<string> Warnings { get; set; } = new List<string>();
            public string Error { get; set; }
        }
    }
}
