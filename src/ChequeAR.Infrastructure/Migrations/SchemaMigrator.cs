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
  DBMS_DATAPUMP.METADATA_FILTER(h, 'SCHEMA_EXPR', 'IN (''' || SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') || ''')');
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
    }
}
