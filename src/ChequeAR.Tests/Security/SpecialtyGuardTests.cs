using System.Collections.Generic;
using ChequeAR.Application.Exceptions;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Security;
using ChequeAR.Domain.Enums;
using Moq;
using Xunit;

namespace ChequeAR.Tests.Security
{
    public class SpecialtyGuardTests
    {
        private static ICurrentUser UserWith(bool authenticated, string role, params string[] specialties)
        {
            var mock = new Mock<ICurrentUser>();
            mock.SetupGet(u => u.IsAuthenticated).Returns(authenticated);
            mock.Setup(u => u.IsInRole(It.IsAny<string>())).Returns((string r) => r == role);
            mock.SetupGet(u => u.Specialties).Returns((IReadOnlyList<string>)specialties);
            return mock.Object;
        }

        [Fact]
        public void Administrador_EsUnrestricted_AunqueNoTengaEspecialidades()
        {
            var guard = new SpecialtyGuard(UserWith(true, Role.Administrador), enforce: true);
            Assert.True(guard.Scope.IsUnrestricted);
            // No lanza para ningún código, ni siquiera uno inexistente.
            guard.EnsureAllows("cualquiera");
        }

        [Fact]
        public void Jefe_QuedaRestringido_NoBypassea()
        {
            // Este test fija la decisión: el bypass anterior (ChequeARSpecialtyAttribute)
            // eximía a Jefe. Si alguien lo restaura, este test falla.
            var guard = new SpecialtyGuard(UserWith(true, Role.Jefe, "cardio"), enforce: true);
            Assert.False(guard.Scope.IsUnrestricted);
            Assert.True(guard.Scope.Allows("cardio"));
            Assert.False(guard.Scope.Allows("onco"));
        }

        [Fact]
        public void Medico_QuedaRestringido()
        {
            var guard = new SpecialtyGuard(UserWith(true, Role.Medico, "reuma"), enforce: true);
            Assert.False(guard.Scope.IsUnrestricted);
            Assert.True(guard.Scope.Allows("reuma"));
        }

        [Fact]
        public void NoAutenticado_Nothing()
        {
            var guard = new SpecialtyGuard(UserWith(false, Role.Medico, "cardio"), enforce: true);
            Assert.True(guard.Scope.IsEmpty);
            Assert.False(guard.Scope.Allows("cardio"));
        }

        [Fact]
        public void NoAdmin_SinEspecialidades_EsEmpty_FailClosed()
        {
            var guard = new SpecialtyGuard(UserWith(true, Role.Medico), enforce: true);
            Assert.True(guard.Scope.IsEmpty);
        }

        [Fact]
        public void MultiEspecialidad_EsUnion()
        {
            var guard = new SpecialtyGuard(UserWith(true, Role.Medico, "cardio", "neumo"), enforce: true);
            Assert.True(guard.Scope.Allows("cardio"));
            Assert.True(guard.Scope.Allows("neumo"));
            Assert.False(guard.Scope.Allows("onco"));
        }

        [Fact]
        public void EnforceFalse_SiempreUnrestricted()
        {
            var guard = new SpecialtyGuard(UserWith(true, Role.Medico, "cardio"), enforce: false);
            Assert.True(guard.Scope.IsUnrestricted);
        }

        [Fact]
        public void EnsureAllows_FueraDeScope_Lanza()
        {
            var guard = new SpecialtyGuard(UserWith(true, Role.Medico, "cardio"), enforce: true);
            Assert.Throws<SpecialtyForbiddenException>(() => guard.EnsureAllows("onco"));
        }

        [Fact]
        public void EnsureAllows_DentroDeScope_NoLanza()
        {
            var guard = new SpecialtyGuard(UserWith(true, Role.Medico, "cardio"), enforce: true);
            guard.EnsureAllows("cardio");
        }
    }
}
