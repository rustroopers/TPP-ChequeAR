using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Results;
using ChequeAR.Api.Controllers.Api;
using ChequeAR.Application.Interfaces;
using Moq;
using Xunit;

namespace ChequeAR.Api.Tests
{
    public class DeployControllerTests
    {
        // Sin Deploy:Key configurada el endpoint no existe, ni siquiera para consultar el estado.
        [Fact]
        public void SinClaveConfigurada_NoExiste()
        {
            var migrator = new Mock<ISchemaMigrator>();
            var controller = new DeployController(migrator.Object) { Request = new HttpRequestMessage(), Configuration = new HttpConfiguration() };
            controller.Request.Headers.Add("X-Deploy-Key", "cualquiera");

            Assert.IsType<NotFoundResult>(controller.Start());
            Assert.IsType<NotFoundResult>(controller.Status());
            migrator.Verify(m => m.Pending(), Times.Never);
        }

        [Fact]
        public void CopyAndPrune_CopiaYBorraLosViejos()
        {
            var migrator = new Mock<ISchemaMigrator>();
            migrator.Setup(m => m.ListBackupFiles()).Returns(new[]
                { "chequear_202610010900.dmp", "chequear_202610050900.dmp", "chequear_202610161800.dmp" });
            var status = new DeployController.MigrationStatus();

            DeployController.CopyAndPrune(migrator.Object, "chequear_202610161800", "https://os/b/o/", "OCI_CRED", 2, status);

            migrator.Verify(m => m.CopyBackup("chequear_202610161800.dmp", "https://os/b/o/", "OCI_CRED"), Times.Once);
            migrator.Verify(m => m.DeleteBackupFile("chequear_202610010900.dmp"), Times.Once);
            Assert.True(status.Copied);
            Assert.Empty(status.Warnings);
        }

        // Sin DBMS_CLOUD (XE local o falta el grant) el deploy sigue, con un aviso.
        [Fact]
        public void CopyAndPrune_SinDbmsCloud_SoloAvisa()
        {
            var migrator = new Mock<ISchemaMigrator>();
            migrator.Setup(m => m.ListBackupFiles()).Throws(new System.InvalidOperationException("PLS-00201"));
            var status = new DeployController.MigrationStatus();

            DeployController.CopyAndPrune(migrator.Object, "chequear_202610161800", null, null, 3, status);

            migrator.Verify(m => m.CopyBackup(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            Assert.Single(status.Warnings);
            Assert.False(status.Copied);
        }
    }
}
