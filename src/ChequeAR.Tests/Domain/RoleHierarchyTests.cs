using System.Linq;
using ChequeAR.Domain.Enums;
using Xunit;

namespace ChequeAR.Tests.Domain
{
    public class RoleHierarchyTests
    {
        [Fact]
        public void Expand_Administrador_IncluyeLosTres()
        {
            var expanded = RoleHierarchy.Expand(Role.Administrador);
            Assert.Equal(new[] { Role.Administrador, Role.Jefe, Role.Medico }, expanded);
        }

        [Fact]
        public void Expand_Jefe_IncluyeMedicoPeroNoAdministrador()
        {
            var expanded = RoleHierarchy.Expand(Role.Jefe);
            Assert.Equal(new[] { Role.Jefe, Role.Medico }, expanded);
        }

        [Fact]
        public void Expand_Medico_SoloMedico()
        {
            var expanded = RoleHierarchy.Expand(Role.Medico);
            Assert.Equal(new[] { Role.Medico }, expanded);
        }

        [Fact]
        public void Expand_RolDesconocido_DegradaAMedico()
        {
            var expanded = RoleHierarchy.Expand("Observador");
            Assert.Equal(new[] { Role.Medico }, expanded);
        }

        [Fact]
        public void Principal_ListaVacia_Medico()
        {
            Assert.Equal(Role.Medico, RoleHierarchy.Principal(Enumerable.Empty<string>()));
        }

        [Fact]
        public void Principal_ListaNula_Medico()
        {
            Assert.Equal(Role.Medico, RoleHierarchy.Principal(null));
        }

        [Fact]
        public void Principal_MezclaDeRoles_DevuelveElMasAlto()
        {
            Assert.Equal(Role.Administrador, RoleHierarchy.Principal(new[] { Role.Medico, Role.Administrador, Role.Jefe }));
            Assert.Equal(Role.Jefe, RoleHierarchy.Principal(new[] { Role.Medico, Role.Jefe }));
        }

        [Fact]
        public void Principal_SoloRolHeredadoDesconocido_Medico()
        {
            Assert.Equal(Role.Medico, RoleHierarchy.Principal(new[] { "Observador" }));
        }

        [Fact]
        public void IsAtLeast_AdminAlcanzaCualquierNivel()
        {
            var roles = new[] { Role.Administrador };
            Assert.True(RoleHierarchy.IsAtLeast(roles, Role.Administrador));
            Assert.True(RoleHierarchy.IsAtLeast(roles, Role.Jefe));
            Assert.True(RoleHierarchy.IsAtLeast(roles, Role.Medico));
        }

        [Fact]
        public void IsAtLeast_MedicoNoAlcanzaJefeNiAdmin()
        {
            var roles = new[] { Role.Medico };
            Assert.False(RoleHierarchy.IsAtLeast(roles, Role.Jefe));
            Assert.False(RoleHierarchy.IsAtLeast(roles, Role.Administrador));
            Assert.True(RoleHierarchy.IsAtLeast(roles, Role.Medico));
        }

        [Fact]
        public void IsAtLeast_ListaVacia_Falso()
        {
            Assert.False(RoleHierarchy.IsAtLeast(System.Array.Empty<string>(), Role.Medico));
        }

        [Fact]
        public void IsAtLeast_RolRequeridoDesconocido_Falso()
        {
            Assert.False(RoleHierarchy.IsAtLeast(new[] { Role.Administrador }, "NoExiste"));
        }
    }
}
