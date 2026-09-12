using System;
using System.Collections.Generic;
using ChequeAR.Application.Fhir;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Xunit;

namespace ChequeAR.Tests.Application
{
    /// <summary>
    /// La forma vieja de los "Bundle" era { resourceType: "Bundle", entry: [entidades] }:
    /// el nombre correcto sobre una estructura que no lo es. Un cliente estándar itera
    /// esperando entry[].resource y con esa forma no encuentra nada.
    /// </summary>
    public class FhirBundleTests
    {
        [Fact]
        public void SearchSet_DeclaraTipoDeBusqueda()
        {
            var bundle = FhirBundles.SearchSet(new[] { Cuestionario("a") }, total: 1, baseUrl: Base);
            Assert.Equal(Bundle.BundleType.Searchset, bundle.Type);
        }

        /// <summary>
        /// El total es el de la COHORTE, no el de la página. De eso depende toda paginación
        /// del lado del cliente: con el total de la página, la última página parece la única.
        /// </summary>
        [Fact]
        public void SearchSet_ElTotalEsElDeLaCohorteNoElDeLaPagina()
        {
            var página = new List<Resource> { Cuestionario("a"), Cuestionario("b") };

            var bundle = FhirBundles.SearchSet(página, total: 240, baseUrl: Base);

            Assert.Equal(240, bundle.Total);
            Assert.Equal(2, bundle.Entry.Count);
        }

        [Fact]
        public void SearchSet_EnvuelveCadaResultadoEnResource()
        {
            var bundle = FhirBundles.SearchSet(new[] { Cuestionario("abc") }, total: 1, baseUrl: Base);

            var entry = Assert.Single(bundle.Entry);
            var recurso = Assert.IsType<Questionnaire>(entry.Resource);
            Assert.Equal("abc", recurso.Id);
        }

        [Fact]
        public void SearchSet_ArmaFullUrlConTipoEId()
        {
            var bundle = FhirBundles.SearchSet(new[] { Cuestionario("abc") }, total: 1, baseUrl: Base);

            Assert.Equal(Base + "/Questionnaire/abc", bundle.Entry[0].FullUrl);
        }

        [Fact]
        public void SearchSet_ToleraBarraFinalEnLaBase()
        {
            var bundle = FhirBundles.SearchSet(new[] { Cuestionario("abc") }, total: 1, baseUrl: Base + "/");
            Assert.Equal(Base + "/Questionnaire/abc", bundle.Entry[0].FullUrl);
        }

        /// <summary>
        /// Sin base conocida se omite fullUrl. Es preferible a emitir una URL relativa, que
        /// sería inválida y engañaría a quien la siga.
        /// </summary>
        [Fact]
        public void SearchSet_SinBase_OmiteFullUrl()
        {
            var bundle = FhirBundles.SearchSet(new[] { Cuestionario("abc") }, total: 1, baseUrl: null);
            Assert.Null(bundle.Entry[0].FullUrl);
        }

        [Fact]
        public void SearchSet_SinResultados_EsUnBundleVacioValido()
        {
            var bundle = FhirBundles.SearchSet(Array.Empty<Resource>(), total: 0, baseUrl: Base);

            Assert.Empty(bundle.Entry);
            Assert.Equal(0, bundle.Total);
        }

        [Fact]
        public void SearchSet_Serializa_ConLaEstructuraQueExigeElEstandar()
        {
            var bundle = FhirBundles.SearchSet(new[] { Cuestionario("abc") }, total: 1, baseUrl: Base);

            var json = new FhirJsonSerializer().SerializeToString(bundle);

            Assert.Contains("\"resourceType\":\"Bundle\"", json);
            Assert.Contains("\"type\":\"searchset\"", json);
            Assert.Contains("\"fullUrl\"", json);
            Assert.Contains("\"resource\"", json);
        }

        private const string Base = "https://localhost:44301/fhir/r5";

        private static Questionnaire Cuestionario(string id)
            => new Questionnaire { Id = id, Title = "Ficha " + id, Status = PublicationStatus.Active };
    }
}
