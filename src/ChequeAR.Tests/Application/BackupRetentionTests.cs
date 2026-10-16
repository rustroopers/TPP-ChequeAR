using ChequeAR.Application.Services;
using Xunit;

namespace ChequeAR.Tests.Application
{
    public class BackupRetentionTests
    {
        [Fact]
        public void ToDelete_ConservaLosUltimosConSuLog()
        {
            var archivos = new[]
            {
                "chequear_202610010900.dmp", "chequear_202610010900.log",
                "chequear_202610051200.dmp", "chequear_202610051200.log",
                "chequear_202610101500.dmp",
                "chequear_202610161800.dmp", "chequear_202610161800.log",
            };

            Assert.Equal(new[] { "chequear_202610010900.dmp", "chequear_202610010900.log" },
                BackupRetention.ToDelete(archivos, 3));
        }

        [Fact]
        public void ToDelete_NoTocaArchivosAjenos()
        {
            var archivos = new[] { "otro.dmp", "chequear_notas.txt", "CHEQUEAR_202601010000.dmp", "chequear_202610161800.dmp" };

            Assert.Empty(BackupRetention.ToDelete(archivos, 1));
        }

        [Fact]
        public void ToDelete_SiempreQuedaAlMenosUno()
        {
            var archivos = new[] { "chequear_202610010900.dmp", "chequear_202610161800.dmp" };

            Assert.Equal(new[] { "chequear_202610010900.dmp" }, BackupRetention.ToDelete(archivos, 0));
        }
    }
}
