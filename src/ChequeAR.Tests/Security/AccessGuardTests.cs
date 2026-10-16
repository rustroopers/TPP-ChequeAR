using System.Collections.Generic;
using ChequeAR.Application.Exceptions;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Security;
using ChequeAR.Domain.Enums;
using Moq;
using Xunit;

namespace ChequeAR.Tests.Security
{
    public class AccessGuardTests
    {
        private static ICurrentUser UserWith(bool authenticated, params string[] roles)
        {
            var mock = new Mock<ICurrentUser>();
            mock.SetupGet(u => u.IsAuthenticated).Returns(authenticated);
            mock.Setup(u => u.IsInRole(It.IsAny<string>())).Returns((string r) =>
                System.Array.IndexOf(roles ?? System.Array.Empty<string>(), r) >= 0);
            mock.SetupGet(u => u.Specialties).Returns((IReadOnlyList<string>)System.Array.Empty<string>());
            mock.SetupGet(u => u.Role).Returns(roles.Length > 0 ? roles[0] : null);
            return mock.Object;
        }

        [Fact]
        public void NoAutenticado_NoPuedeNada()
        {
            var guard = new AccessGuard(UserWith(false, Role.Administrador));
            Assert.False(guard.Can(Permissions.QuestionnaireRead));
        }

        [Fact]
        public void UsuarioNulo_NoPuedeNada()
        {
            var guard = new AccessGuard(null);
            Assert.False(guard.Can(Permissions.QuestionnaireRead));
        }

        [Fact]
        public void RolDesconocido_NoPuedeNada()
        {
            // IsInRole nunca devuelve true para ninguno de los 3 roles conocidos.
            var guard = new AccessGuard(UserWith(true));
            Assert.False(guard.Can(Permissions.QuestionnaireRead));
        }

        [Fact]
        public void Administrador_PuedeTodo()
        {
            var guard = new AccessGuard(UserWith(true, Role.Administrador));
            Assert.True(guard.Can(Permissions.AuditRead));
            Assert.True(guard.Can(Permissions.UserManage));
            Assert.True(guard.Can(Permissions.QuestionnaireDelete));
        }

        // IsInRole viene expandido por jerarquía: decidir con él haría que un permiso quitado a
        // Jefe pero que tiene Médico siguiera valiendo para el Jefe.
        [Fact]
        public void Permiso_SaleDelRolDelUsuario_NoDeLosHeredados()
        {
            var jefe = new Mock<ICurrentUser>();
            jefe.SetupGet(u => u.IsAuthenticated).Returns(true);
            jefe.SetupGet(u => u.Role).Returns((string)null);
            jefe.Setup(u => u.IsInRole(It.IsAny<string>())).Returns(true);

            Assert.False(new AccessGuard(jefe.Object).Can(Permissions.QuestionnaireRead));
        }

        [Fact]
        public void Jefe_NoPuedeAuditoriaNiUsuarios()
        {
            var guard = new AccessGuard(UserWith(true, Role.Jefe, Role.Medico)); // claims expandidos
            Assert.False(guard.Can(Permissions.AuditRead));
            Assert.False(guard.Can(Permissions.UserManage));
            Assert.True(guard.Can(Permissions.ReportRead));
        }

        [Fact]
        public void Medico_NoPuedeEscribirCuestionariosNiVerReportes()
        {
            var guard = new AccessGuard(UserWith(true, Role.Medico));
            Assert.False(guard.Can(Permissions.QuestionnaireWrite));
            Assert.False(guard.Can(Permissions.ReportRead));
            Assert.True(guard.Can(Permissions.QuestionnaireRead));
            Assert.True(guard.Can(Permissions.EncounterWrite));
        }

        [Fact]
        public void EnsureCan_SinPermiso_Lanza()
        {
            var guard = new AccessGuard(UserWith(true, Role.Medico));
            var ex = Assert.Throws<PermissionForbiddenException>(() => guard.EnsureCan(Permissions.ReportRead));
            Assert.Equal(Permissions.ReportRead, ex.Permission);
        }

        [Fact]
        public void EnsureCan_ConPermiso_NoLanza()
        {
            var guard = new AccessGuard(UserWith(true, Role.Medico));
            guard.EnsureCan(Permissions.QuestionnaireRead);
        }
    }
}
