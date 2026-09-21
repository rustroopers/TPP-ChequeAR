using ChequeAR.Application.Security;
using ChequeAR.Domain.Enums;
using Xunit;

namespace ChequeAR.Tests.Security
{
    /// <summary>
    /// Un caso por celda de la matriz del requisito (ver plan "Segmentación por roles").
    /// Este archivo ES la traducción ejecutable de "Admin todo, Jefe todo menos auditoría y
    /// usuarios, Medico sólo lectura de cuestionarios + carga de fichas".
    /// </summary>
    public class RolePolicyTests
    {
        // ---- Administrador: todo ----

        [Theory]
        [InlineData(Permissions.QuestionnaireRead)]
        [InlineData(Permissions.QuestionnaireWrite)]
        [InlineData(Permissions.QuestionnaireDelete)]
        [InlineData(Permissions.EncounterWrite)]
        [InlineData(Permissions.ReportRead)]
        [InlineData(Permissions.ReportWrite)]
        [InlineData(Permissions.AuditRead)]
        [InlineData(Permissions.UserManage)]
        [InlineData(Permissions.IntegrationRead)]
        [InlineData(Permissions.TerminologyExpand)]
        public void Administrador_TieneTodosLosPermisos(string permission)
        {
            Assert.True(RolePolicy.Allows(new[] { Role.Administrador }, permission));
        }

        // ---- Jefe: todo salvo auditoría, usuarios y borrar cuestionarios ----

        [Theory]
        [InlineData(Permissions.QuestionnaireRead)]
        [InlineData(Permissions.QuestionnaireWrite)]
        [InlineData(Permissions.EncounterWrite)]
        [InlineData(Permissions.ReportRead)]
        [InlineData(Permissions.ReportWrite)]
        [InlineData(Permissions.IntegrationRead)]
        [InlineData(Permissions.TerminologyExpand)]
        public void Jefe_TieneSuPermiso(string permission)
        {
            Assert.True(RolePolicy.Allows(new[] { Role.Jefe }, permission));
        }

        [Theory]
        [InlineData(Permissions.QuestionnaireDelete)]
        [InlineData(Permissions.AuditRead)]
        [InlineData(Permissions.UserManage)]
        public void Jefe_NoTienePermiso(string permission)
        {
            Assert.False(RolePolicy.Allows(new[] { Role.Jefe }, permission));
        }

        // ---- Medico: lectura de cuestionarios + carga de fichas, nada más ----

        [Theory]
        [InlineData(Permissions.QuestionnaireRead)]
        [InlineData(Permissions.EncounterWrite)]
        [InlineData(Permissions.TerminologyExpand)]
        public void Medico_TieneSuPermiso(string permission)
        {
            Assert.True(RolePolicy.Allows(new[] { Role.Medico }, permission));
        }

        [Theory]
        [InlineData(Permissions.QuestionnaireWrite)]
        [InlineData(Permissions.QuestionnaireDelete)]
        [InlineData(Permissions.ReportRead)]
        [InlineData(Permissions.ReportWrite)]
        [InlineData(Permissions.AuditRead)]
        [InlineData(Permissions.UserManage)]
        [InlineData(Permissions.IntegrationRead)]
        public void Medico_NoTienePermiso(string permission)
        {
            Assert.False(RolePolicy.Allows(new[] { Role.Medico }, permission));
        }

        // ---- Casos generales ----

        [Fact]
        public void Allows_ListaVacia_Falso()
        {
            Assert.False(RolePolicy.Allows(System.Array.Empty<string>(), Permissions.QuestionnaireRead));
        }

        [Fact]
        public void Allows_ListaNula_Falso()
        {
            Assert.False(RolePolicy.Allows(null, Permissions.QuestionnaireRead));
        }

        [Fact]
        public void Allows_RolDesconocido_Falso()
        {
            Assert.False(RolePolicy.Allows(new[] { "Observador" }, Permissions.QuestionnaireRead));
        }

        [Fact]
        public void Allows_UnionDeRoles_BastaConUno()
        {
            // Simula los claims expandidos de un Jefe (Jefe + Medico): alcanza con que uno
            // de los dos otorgue el permiso.
            Assert.True(RolePolicy.Allows(new[] { Role.Jefe, Role.Medico }, Permissions.ReportRead));
        }

        [Fact]
        public void PermissionsOf_RolDesconocido_Vacio()
        {
            Assert.Empty(RolePolicy.PermissionsOf("NoExiste"));
        }

        [Fact]
        public void PermissionsOf_RolNulo_Vacio()
        {
            Assert.Empty(RolePolicy.PermissionsOf(null));
        }
    }
}
