using System.Collections.Generic;
using System.Configuration;
using System.Data.Entity.Migrations;
using System.Linq;
using ChequeAR.Application.Interfaces;
using Oracle.ManagedDataAccess.Client;

namespace ChequeAR.Infrastructure.Migrations
{
    public class SchemaMigrator : ISchemaMigrator
    {
        // Un export del esquema tarda minutos aunque la base sea chica.
        private const int BackupTimeoutSeconds = 1800;

        public IReadOnlyList<string> Pending() => new DbMigrator(new Configuration()).GetPendingMigrations().ToList();

        public IReadOnlyList<string> Apply()
        {
            var migrator = new DbMigrator(new Configuration());
            var pending = migrator.GetPendingMigrations().ToList();
            migrator.Update();
            return pending;
        }

        /// <summary>
        /// La Autonomous Always Free no tiene backups ni restore: el único resguardo es un
        /// export del esquema dentro de la misma base. Necesita READ y WRITE sobre DATA_PUMP_DIR.
        /// </summary>
        public string Backup(string fileName)
        {
            using (var conn = new OracleConnection(ConfigurationManager.ConnectionStrings["ChequeAR"].ConnectionString))
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandTimeout = BackupTimeoutSeconds;
                cmd.CommandText = @"DECLARE
  h NUMBER;
  estado VARCHAR2(30);
BEGIN
  h := DBMS_DATAPUMP.OPEN(operation => 'EXPORT', job_mode => 'SCHEMA');
  DBMS_DATAPUMP.ADD_FILE(h, :archivo || '.dmp', 'DATA_PUMP_DIR', reusefile => 1);
  DBMS_DATAPUMP.ADD_FILE(h, :archivo || '.log', 'DATA_PUMP_DIR', filetype => DBMS_DATAPUMP.KU$_FILE_TYPE_LOG_FILE, reusefile => 1);
  DBMS_DATAPUMP.METADATA_FILTER(h, 'SCHEMA_EXPR', 'IN (''CHEQUEAR'')');
  DBMS_DATAPUMP.START_JOB(h);
  DBMS_DATAPUMP.WAIT_FOR_JOB(h, estado);
  IF estado <> 'COMPLETED' THEN
    RAISE_APPLICATION_ERROR(-20002, 'El export terminó en estado ' || estado);
  END IF;
END;";
                cmd.BindByName = true;
                cmd.Parameters.Add("archivo", OracleDbType.Varchar2).Value = fileName;
                conn.Open();
                cmd.ExecuteNonQuery();
            }
            return fileName + ".dmp";
        }

        // Los tres necesitan EXECUTE sobre DBMS_CLOUD; la copia, además, una credencial de OCI
        // creada con DBMS_CLOUD.CREATE_CREDENTIAL.
        public IReadOnlyList<string> ListBackupFiles()
        {
            var archivos = new List<string>();
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT object_name FROM DBMS_CLOUD.LIST_FILES('DATA_PUMP_DIR')";
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) archivos.Add(r.GetString(0));
            }
            return archivos;
        }

        public void DeleteBackupFile(string fileName) => Execute(
            "BEGIN DBMS_CLOUD.DELETE_FILE(directory_name => 'DATA_PUMP_DIR', file_name => :archivo); END;",
            ("archivo", fileName));

        public void CopyBackup(string fileName, string objectUriBase, string credentialName) => Execute(
            "BEGIN DBMS_CLOUD.PUT_OBJECT(credential_name => :credencial, object_uri => :uri, directory_name => 'DATA_PUMP_DIR', file_name => :archivo); END;",
            ("credencial", credentialName), ("uri", objectUriBase.TrimEnd('/') + "/" + fileName), ("archivo", fileName));

        private static OracleConnection Open()
        {
            var conn = new OracleConnection(ConfigurationManager.ConnectionStrings["ChequeAR"].ConnectionString);
            conn.Open();
            return conn;
        }

        private static void Execute(string plsql, params (string Name, string Value)[] parameters)
        {
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandTimeout = BackupTimeoutSeconds;
                cmd.CommandText = plsql;
                cmd.BindByName = true;
                foreach (var p in parameters)
                    cmd.Parameters.Add(p.Name, OracleDbType.Varchar2).Value = p.Value;
                cmd.ExecuteNonQuery();
            }
        }
    }
}
