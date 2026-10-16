using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using ChequeAR.Application.Reports;
using ChequeAR.Domain.Entities;
using Xunit;

namespace ChequeAR.Tests.Architecture
{
    /// <summary>
    /// Convierte en una prueba lo que el documento de arquitectura afirma: que modificaciones
    /// en la infraestructura o en la persistencia no afectan la lógica de negocio.
    ///
    /// Sin esto, la afirmación es una convención escrita que se erosiona con el primer
    /// `using` conveniente. Con esto, romperla rompe el build.
    /// </summary>
    public class LayerDependencyTests
    {
        [Theory]
        [InlineData("Oracle.ManagedDataAccess")]
        [InlineData("EntityFramework")]
        [InlineData("System.Web")]
        [InlineData("Microsoft.Owin")]
        public void CapaDeNegocio_NoDependeDeLaPersistencia(string ensambladoProhibido)
        {
            var referencias = typeof(QueryBuilder).Assembly
                .GetReferencedAssemblies()
                .Select(a => a.Name)
                .ToList();

            Assert.False(
                referencias.Any(r => r.StartsWith(ensambladoProhibido, StringComparison.OrdinalIgnoreCase)),
                "ChequeAR.Application referencia '" + ensambladoProhibido + "'. La lógica de negocio "
                + "no debe conocer el motor de base de datos: mové el acceso a datos detrás de una "
                + "interfaz declarada en Application e implementada en Infrastructure.");
        }

        /// <summary>
        /// GetReferencedAssemblies sólo ve lo que se usa en el IL: una referencia agregada al
        /// csproj y todavía sin usar pasaba. Acá se mira el csproj.
        /// </summary>
        [Theory]
        [InlineData("Oracle.ManagedDataAccess")]
        [InlineData("EntityFramework")]
        [InlineData("System.Web")]
        [InlineData("System.Data")]
        [InlineData("Microsoft.Owin")]
        public void CapaDeNegocio_ElCsprojNoReferenciaInfraestructura(string prohibido)
        {
            var csproj = XDocument.Load(Path.Combine(SrcDirectory(), "ChequeAR.Application", "ChequeAR.Application.csproj"));
            var referencias = csproj.Descendants()
                .Where(e => e.Name.LocalName == "Reference" || e.Name.LocalName == "PackageReference")
                .Select(e => (string)e.Attribute("Include") ?? "")
                .ToList();
            Assert.NotEmpty(referencias);

            Assert.DoesNotContain(referencias, r => r.StartsWith(prohibido, StringComparison.OrdinalIgnoreCase));
        }

        private static string SrcDirectory()
        {
            for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "ChequeAR.Application", "ChequeAR.Application.csproj"))) return dir.FullName;
            throw new DirectoryNotFoundException("No se encontró ChequeAR.Application.csproj");
        }

        /// <summary>
        /// El dominio es el corazón: si empieza a depender de algo, deja de ser portable y
        /// arrastra a todos los demás.
        /// </summary>
        [Fact]
        public void Dominio_NoDependeDeNadaFueraDelFramework()
        {
            var referencias = typeof(Questionnaire).Assembly
                .GetReferencedAssemblies()
                .Select(a => a.Name)
                .Where(n => !n.StartsWith("System", StringComparison.OrdinalIgnoreCase)
                         && !n.StartsWith("mscorlib", StringComparison.OrdinalIgnoreCase)
                         && !n.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.True(referencias.Count == 0,
                "ChequeAR.Domain referencia: " + string.Join(", ", referencias));
        }

        /// <summary>
        /// La barrera que hace verificable la afirmación sobre testing independiente: este
        /// propio proyecto no puede ver infraestructura ni presentación. Si alguien agregara
        /// esas referencias, las pruebas dejarían de probar aislamiento.
        /// </summary>
        [Theory]
        [InlineData("ChequeAR.Infrastructure")]
        [InlineData("ChequeAR.Api")]
        public void LasPruebas_NoVenInfraestructuraNiPresentacion(string capa)
        {
            var referencias = typeof(LayerDependencyTests).Assembly
                .GetReferencedAssemblies()
                .Select(a => a.Name);

            Assert.DoesNotContain(capa, referencias);
        }
    }
}
