using ChequeAR.Application.Security;
using Xunit;

namespace ChequeAR.Tests.Security
{
    public class SpecialtyScopeTests
    {
        [Fact]
        public void Nothing_NoPermiteNada()
        {
            var scope = SpecialtyScope.Nothing;
            Assert.False(scope.Allows("cardio"));
            Assert.False(scope.Allows(null));
            Assert.False(scope.Allows(""));
            Assert.True(scope.IsEmpty);
        }

        [Fact]
        public void Limited_Vacio_EsEmpty_YNoPermiteNada()
        {
            var scope = SpecialtyScope.Limited(new string[0]);
            Assert.True(scope.IsEmpty);
            Assert.False(scope.Allows("cardio"));
        }

        [Fact]
        public void Limited_NoPermiteNullNiVacio_AunqueTengaCodigos()
        {
            var scope = SpecialtyScope.Limited(new[] { "cardio" });
            // Un recurso sin especialidad NO es "de todos": con scope limitado, null/"" no pasan.
            Assert.False(scope.Allows(null));
            Assert.False(scope.Allows(""));
            Assert.True(scope.Allows("cardio"));
            Assert.False(scope.Allows("onco"));
        }

        [Fact]
        public void Limited_EsCaseInsensitive()
        {
            var scope = SpecialtyScope.Limited(new[] { "cardio" });
            Assert.True(scope.Allows("CARDIO"));
            Assert.True(scope.Allows("Cardio"));
        }

        [Fact]
        public void Unrestricted_PermiteCualquierCosa_IncluidoNull()
        {
            var scope = SpecialtyScope.Unrestricted();
            Assert.True(scope.IsUnrestricted);
            Assert.False(scope.IsEmpty);
            Assert.True(scope.Allows("cardio"));
            Assert.True(scope.Allows(null));
            Assert.True(scope.Allows(""));
            Assert.True(scope.Allows("codigo-inexistente"));
        }

        [Fact]
        public void Limited_Union_DeVariasEspecialidades()
        {
            var scope = SpecialtyScope.Limited(new[] { "cardio", "neumo" });
            Assert.True(scope.Allows("cardio"));
            Assert.True(scope.Allows("neumo"));
            Assert.False(scope.Allows("onco"));
        }
    }
}
