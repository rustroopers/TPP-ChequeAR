using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Security;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using Moq;
using Xunit;

namespace ChequeAR.Tests.Application
{
    /// <summary>Lectura del paciente desde la vista del hospital y qué pasa cuando no responde.</summary>
    public class PatientServiceHospitalTests
    {
        private readonly Mock<IOracleViewClient> _oracle = new Mock<IOracleViewClient>();
        private readonly Mock<IQuestionnaireResponseRepository> _responses = new Mock<IQuestionnaireResponseRepository>();

        private PatientService Sut() => new PatientService(
            Mock.Of<IHospitalFhirClient>(), _oracle.Object, _responses.Object,
            Mock.Of<IQuestionnaireRepository>(), TestHelpers.CatalogWith(), TestHelpers.GuardFor("cardio"));

        public static IReadOnlyList<IDictionary<string, object>> Filas(params Dictionary<string, object>[] filas) => filas;

        private static Dictionary<string, object> Fila(string id, string apellido, string nombre, DateTime? nacimiento = null, string sexo = null)
            => new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["ID_PACIENTE"] = id, ["APELLIDO"] = apellido, ["NOMBRE"] = nombre,
                ["DOCUMENTO"] = "40111222", ["FECHA_NACIMIENTO"] = nacimiento, ["SEXO"] = sexo
            };

        [Fact]
        public async Task GetAsync_ConLaVistaDelHospital_ArmaElPaciente()
        {
            _oracle.Setup(o => o.QueryAsync("VW_PACIENTE", It.IsAny<IReadOnlyDictionary<string, object>>()))
                   .ReturnsAsync(Filas(Fila("PAC-1", "Suarez", "Bruno", new DateTime(2013, 3, 5))));

            var hit = await Sut().GetAsync("PAC-1");

            Assert.Equal("PAC-1", hit.HospitalId);
            Assert.Equal("Suarez, Bruno", hit.FullName);
            Assert.Equal("05/03/2013", hit.BirthDateLabel);
        }

        [Fact]
        public async Task GetAsync_HospitalCaido_SinFichasPropias_Null()
        {
            _oracle.Setup(o => o.QueryAsync(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object>>()))
                   .ThrowsAsync(new InvalidOperationException("ORA-12541"));
            _responses.Setup(r => r.SearchAsync("PAC-1", null, null, 0, 50, It.IsAny<SpecialtyScope>()))
                      .ReturnsAsync(new List<QuestionnaireResponse>());

            Assert.Null(await Sut().GetAsync("PAC-1"));
        }

        [Fact]
        public async Task TryGetAsync_DatosParaEmbeberEnLaFicha()
        {
            _oracle.Setup(o => o.QueryAsync("VW_PACIENTE", It.IsAny<IReadOnlyDictionary<string, object>>()))
                   .ReturnsAsync(Filas(Fila("PAC-1", "Suarez", "Bruno", new DateTime(2013, 3, 5), "M")));

            var d = await Sut().TryGetAsync("PAC-1");

            Assert.Equal("Suarez, Bruno", d.FullName);
            Assert.Equal("male", d.Gender);
            Assert.Equal(new DateTime(2013, 3, 5), d.BirthDate);
        }

        // Sin nombre o sin hospital no se embebe nada: la HC no es un nombre.
        [Fact]
        public async Task TryGetAsync_SinNombreOHospitalCaido_Null()
        {
            _oracle.SetupSequence(o => o.QueryAsync("VW_PACIENTE", It.IsAny<IReadOnlyDictionary<string, object>>()))
                   .ReturnsAsync(Filas(Fila("PAC-1", null, null)))
                   .ThrowsAsync(new InvalidOperationException("ORA-12541"));

            Assert.Null(await Sut().TryGetAsync("PAC-1"));
            Assert.Null(await Sut().TryGetAsync("PAC-1"));
            Assert.Null(await Sut().TryGetAsync(" "));
        }

        [Fact]
        public async Task SearchAsync_TextoMuyCorto_NoConsulta()
        {
            Assert.Empty(await Sut().SearchAsync("a"));
            _oracle.Verify(o => o.SearchAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task SearchAsync_ConElHospital_DevuelveSusFilas()
        {
            _oracle.Setup(o => o.SearchAsync("VW_PACIENTE", It.IsAny<IEnumerable<string>>(), "suar", It.IsAny<int>()))
                   .ReturnsAsync(Filas(Fila("PAC-1", "Suarez", "Bruno"), Fila("PAC-2", "Suarez", "Ana")));

            var hits = await Sut().SearchAsync("suar");

            Assert.Equal(new[] { "PAC-1", "PAC-2" }, new[] { hits[0].HospitalId, hits[1].HospitalId });
        }
    }

    public class TerminologyServiceTests
    {
        [Fact]
        public async Task SuggestAsync_MenosDeTresLetras_NoConsulta()
        {
            var client = new Mock<ITerminologyClient>();

            Assert.Empty(await new TerminologyService(client.Object).SuggestAsync("do", 10));
            client.Verify(c => c.ExpandSnomedAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task SuggestAsync_LeeLaExpansion()
        {
            var client = new Mock<ITerminologyClient>();
            client.Setup(c => c.ExpandSnomedAsync("dolor", 10)).ReturnsAsync(
                "{\"expansion\":{\"contains\":[{\"code\":\"22253000\",\"display\":\"dolor\"},{\"display\":\"sin código\"}]}}");

            var s = await new TerminologyService(client.Object).SuggestAsync(" dolor ", 10);

            var unico = Assert.Single(s);
            Assert.Equal("22253000", unico.Code);
        }

        [Fact]
        public async Task ExpandAsync_DevuelveUnValueSetConLaExpansion()
        {
            var client = new Mock<ITerminologyClient>();
            client.Setup(c => c.ExpandSnomedAsync("dolor", 50)).ReturnsAsync(
                "{\"expansion\":{\"contains\":[{\"code\":\"22253000\",\"display\":\"dolor\"}]}}");
            var ahora = new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

            var vs = await new TerminologyService(client.Object).ExpandAsync("dolor", 500, ahora);

            Assert.Equal("http://snomed.info/sct?fhir_vs", vs.Url);
            var c1 = Assert.Single(vs.Expansion.Contains);
            Assert.Equal("http://snomed.info/sct", c1.System);
            Assert.Equal("22253000", c1.Code);
            Assert.Contains(vs.Expansion.Parameter, p => p.Name == "count" && ((Hl7.Fhir.Model.Integer)p.Value).Value == 50);
            Assert.NotNull(new Hl7.Fhir.Serialization.FhirJsonSerializer().SerializeToString(vs));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("<html>")]
        [InlineData("{\"expansion\":{}}")]
        public void Interpretar_RespuestaInvalida_Vacio(string json)
        {
            Assert.Empty(TerminologyService.Interpretar(json));
        }
    }
}
