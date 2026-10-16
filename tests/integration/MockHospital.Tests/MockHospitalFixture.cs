using System.Globalization;
using System;
using System.Collections.Generic;
using System.Data;
using Oracle.ManagedDataAccess.Client;
using Xunit;

namespace ChequeAR.Tests.MockHospital
{
    /// <summary>
    /// Fixture compartido para los tests de integración del mock hospital.
    /// Asume que el contenedor `infra/mock-hospital/docker-compose.yml` está corriendo
    /// y reportado como `healthy`. Si no, los tests fallan rápido y claro.
    ///
    /// La connection string se toma de la env var MOCK_HOSPITAL_CONN; si no está,
    /// se usa un default apuntando a localhost:1521/XEPDB1 con CHEQUEAR_RO/devdev.
    /// </summary>
    public sealed class MockHospitalFixture
    {
        public string ConnectionStringRO { get; }
        public string ConnectionStringOwner { get; }

        public MockHospitalFixture()
        {
            ConnectionStringRO = Environment.GetEnvironmentVariable("MOCK_HOSPITAL_CONN")
                ?? "User Id=CHEQUEAR_RO;Password=devdev;Data Source=localhost:1521/XEPDB1;";

            ConnectionStringOwner = Environment.GetEnvironmentVariable("MOCK_HOSPITAL_OWNER_CONN")
                ?? "User Id=HOSPITAL_MOCK;Password=hospitalmockdev;Data Source=localhost:1521/XEPDB1;";

            EnsureReachable();
        }

        private void EnsureReachable()
        {
            try
            {
                using (var conn = new OracleConnection(ConnectionStringRO))
                using (var cmd = new OracleCommand("SELECT 1 FROM DUAL", conn))
                {
                    conn.Open();
                    cmd.ExecuteScalar();
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "El contenedor mock-hospital no responde. " +
                    "Levantarlo con `docker compose up -d --wait` desde infra/mock-hospital/ " +
                    "antes de correr esta suite.", ex);
            }
        }

        public long ScalarCount(string viewName)
        {
            using (var conn = new OracleConnection(ConnectionStringRO))
            using (var cmd = new OracleCommand($"SELECT COUNT(*) FROM HOSPITAL_MOCK.{viewName}", conn))
            {
                conn.Open();
                return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }

        public string ScalarString(string sql, string connectionString = null)
        {
            using (var conn = new OracleConnection(connectionString ?? ConnectionStringRO))
            using (var cmd = new OracleCommand(sql, conn))
            {
                conn.Open();
                var result = cmd.ExecuteScalar();
                return result?.ToString();
            }
        }

        public IReadOnlyList<string> CollectStrings(string sql)
        {
            var rows = new List<string>();
            using (var conn = new OracleConnection(ConnectionStringRO))
            using (var cmd = new OracleCommand(sql, conn))
            {
                conn.Open();
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        rows.Add(reader.IsDBNull(0) ? null : reader.GetValue(0)?.ToString());
                    }
                }
            }
            return rows;
        }
    }

    [CollectionDefinition("MockHospital")]
    public sealed class MockHospitalCollection : ICollectionFixture<MockHospitalFixture> { }
}
