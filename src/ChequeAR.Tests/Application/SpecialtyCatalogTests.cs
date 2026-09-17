using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using Moq;
using Xunit;

namespace ChequeAR.Tests.Application
{
    /// <summary>Reemplaza a SpecialtyMapTests: mismo contrato de Label, ahora sobre el catálogo.</summary>
    public class SpecialtyCatalogTests
    {
        private static SpecialtyCatalog CatalogConLos5()
            => new SpecialtyCatalog(new[]
            {
                new Specialty { Code = "general", Name = "General", IsActive = true, IsDefault = true, Color = "green" },
                new Specialty { Code = "cardio", Name = "Cardiología", IsActive = true, Color = "red" },
                new Specialty { Code = "neumo", Name = "Neumonología", IsActive = true, Color = "blue" },
                new Specialty { Code = "onco", Name = "Oncología", IsActive = true, Color = "violet" },
                new Specialty { Code = "reuma", Name = "Reumatología", IsActive = true, Color = "yellow" }
            });

        [Fact]
        public void Label_DisplayGana_SiempreQueVengaConValor()
        {
            var cat = CatalogConLos5();
            Assert.Equal("Reumatología Pediátrica", cat.Label("reuma", "Reumatología Pediátrica"));
        }

        [Theory]
        [InlineData("general", "General")]
        [InlineData("cardio", "Cardiología")]
        [InlineData("neumo", "Neumonología")]
        [InlineData("onco", "Oncología")]
        [InlineData("reuma", "Reumatología")]
        public void Label_CaeAlCatalogo_SinDisplay(string code, string expected)
        {
            Assert.Equal(expected, CatalogConLos5().Label(code));
        }

        [Fact]
        public void Label_EsCaseInsensitive_YTrimea()
        {
            Assert.Equal("Cardiología", CatalogConLos5().Label("  CARDIO  "));
        }

        [Fact]
        public void Label_CodigoDesconocido_CaeAlCodigoCrudo()
        {
            Assert.Equal("hemato", CatalogConLos5().Label("hemato"));
        }

        [Fact]
        public void Label_SinCodigo_CaeAlDefaultLabel()
        {
            Assert.Equal("General", CatalogConLos5().Label(null));
        }

        [Fact]
        public void Color_UsaLaColumna_CuandoEsta()
        {
            Assert.Equal("red", CatalogConLos5().Color("cardio"));
        }

        [Fact]
        public void Color_CodigoDesconocido_EsEstable_YCaeEnZoneColors()
        {
            var cat = CatalogConLos5();
            var c1 = cat.Color("hemato");
            var c2 = cat.Color("hemato");
            Assert.Equal(c1, c2);
            Assert.Contains(c1, SpecialtyCatalog.ZoneColors);
        }

        [Fact]
        public void DefaultCode_ConIsDefault_UsaEsaFila()
        {
            Assert.Equal("general", CatalogConLos5().DefaultCode);
        }

        [Fact]
        public void DefaultCode_SinIsDefault_CaeAlaPrimera()
        {
            var cat = new SpecialtyCatalog(new[]
            {
                new Specialty { Code = "cardio", Name = "Cardiología", IsActive = true },
                new Specialty { Code = "onco", Name = "Oncología", IsActive = true }
            });
            Assert.Equal("cardio", cat.DefaultCode);
        }

        [Fact]
        public void Catalogo_Vacio_CaeAlDefaultDeDominio()
        {
            var cat = SpecialtyCatalog.Empty;
            Assert.Equal("general", cat.DefaultCode);
            Assert.Equal("General", cat.DefaultLabel);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        public void CodeOrDefault_SinCodigo_UsaElDefault(string code)
        {
            Assert.Equal("general", CatalogConLos5().CodeOrDefault(code));
        }

        [Fact]
        public void CodeOrDefault_ConCodigo_LoConserva()
        {
            Assert.Equal("cardio", CatalogConLos5().CodeOrDefault("cardio"));
        }

        [Fact]
        public void ExistsActive_FalseParaInactiva()
        {
            var cat = new SpecialtyCatalog(new[]
            {
                new Specialty { Code = "onco", Name = "Oncología", IsActive = false }
            });
            Assert.False(cat.ExistsActive("onco"));
            Assert.True(cat.Exists("onco"));
        }
    }

    public class SpecialtyCatalogServiceTests
    {
        private static Specialty[] Los5() => new[]
        {
            new Specialty { Code = "general", Name = "General", IsActive = true, IsDefault = true },
            new Specialty { Code = "cardio", Name = "Cardiología", IsActive = true }
        };

        [Fact]
        public async Task GetAsync_CacheaDentroDelTtl_UnaSolaLlamadaAlRepo()
        {
            var repo = new Mock<ISpecialtyRepository>();
            repo.Setup(r => r.ListAsync(true)).ReturnsAsync((IReadOnlyList<Specialty>)Los5().ToList());
            var svc = new SpecialtyCatalogService(() => repo.Object, ttlMinutes: 10);

            await svc.GetAsync();
            await svc.GetAsync();

            repo.Verify(r => r.ListAsync(true), Times.Once);
        }

        [Fact]
        public async Task Invalidate_FuerzaRecarga()
        {
            var repo = new Mock<ISpecialtyRepository>();
            repo.Setup(r => r.ListAsync(true)).ReturnsAsync((IReadOnlyList<Specialty>)Los5().ToList());
            var svc = new SpecialtyCatalogService(() => repo.Object, ttlMinutes: 10);

            await svc.GetAsync();
            svc.Invalidate();
            await svc.GetAsync();

            repo.Verify(r => r.ListAsync(true), Times.Exactly(2));
        }

        [Fact]
        public async Task SiElRepoFalla_DevuelveElSnapshotAnterior_SinPropagar()
        {
            var repo = new Mock<ISpecialtyRepository>();
            var callCount = 0;
            repo.Setup(r => r.ListAsync(true)).Returns(() =>
            {
                callCount++;
                if (callCount == 1) return Task.FromResult((IReadOnlyList<Specialty>)Los5().ToList());
                throw new System.Exception("Oracle caído");
            });
            // TTL corto para forzar una segunda carga rápido en el test.
            var svc = new SpecialtyCatalogService(() => repo.Object, ttlMinutes: 0);

            var first = await svc.GetAsync();
            var second = await svc.GetAsync();   // el repo tira acá, pero no debe propagar

            Assert.Equal(first.Items.Count, second.Items.Count);
            Assert.True(second.Items.Count > 0);
        }

        [Fact]
        public async Task NuncaHayaCargado_DevuelveEmpty_SiElRepoFallaDesdeElInicio()
        {
            var repo = new Mock<ISpecialtyRepository>();
            repo.Setup(r => r.ListAsync(true)).ThrowsAsync(new System.Exception("Oracle caído"));
            var svc = new SpecialtyCatalogService(() => repo.Object, ttlMinutes: 10);

            var cat = await svc.GetAsync();

            Assert.Equal("general", cat.DefaultCode);
            Assert.Empty(cat.Items);
        }
    }
}
