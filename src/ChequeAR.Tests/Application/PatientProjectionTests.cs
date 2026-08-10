using System;
using System.Linq;
using ChequeAR.Application.Fhir;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using Xunit;

namespace ChequeAR.Tests.Application
{
    /// <summary>
    /// Paciente y encuentro son entidades que ChequeAR NO posee: las deriva. La proyección
    /// tiene que dejar eso explícito en el recurso, identificando siempre por historia
    /// clínica y nunca por una referencia resoluble que no podríamos cumplir.
    /// </summary>
    public class PatientProjectionTests
    {
        [Fact]
        public void Paciente_SeIdentificaPorHistoriaClinica()
        {
            var p = FhirPatientProjection.ToPatient(new PatientSearchHit
            {
                HospitalId = "PAC-000042",
                FullName = "Pérez, Ana"
            });

            var identificador = Assert.Single(p.Identifier);
            Assert.Equal("https://chequear.garrahan/patient-hospital-id", identificador.System);
            Assert.Equal("PAC-000042", identificador.Value);
            Assert.Equal("Pérez, Ana", p.Name.Single().Text);
        }

        /// <summary>
        /// En modo degradado el nombre puede no estar. Se emite el recurso igual con su
        /// identificador: es información real y parcial, no inventada.
        /// </summary>
        [Fact]
        public void PacienteSinNombre_SeEmiteIgualConSuIdentificador()
        {
            var p = FhirPatientProjection.ToPatient(new PatientSearchHit { HospitalId = "PAC-000001" });

            Assert.Empty(p.Name);
            Assert.Equal("PAC-000001", p.Identifier.Single().Value);
        }

        [Fact]
        public void Encuentro_SeDerivaDeLaFichaYApuntaAlPacientePorIdentificador()
        {
            var ficha = new QuestionnaireResponse
            {
                Id = Guid.NewGuid(),
                PatientHospitalId = "PAC-000042",
                AuthoredAt = new DateTime(2026, 8, 1, 10, 30, 0, DateTimeKind.Utc)
            };

            var e = FhirPatientProjection.ToEncounter(ficha);

            Assert.Equal(ficha.Id.ToString(), e.Id);
            Assert.Equal("PAC-000042", e.Subject.Identifier.Value);
            Assert.Contains("2026-08-01", e.ActualPeriod.Start);
        }

        [Fact]
        public void Proyecciones_IgnoranNulosEnVezDeExplotar()
        {
            Assert.Null(FhirPatientProjection.ToPatient(null));
            Assert.Null(FhirPatientProjection.ToEncounter(null));
            Assert.Empty(FhirPatientProjection.ToPatients(null));
            Assert.Empty(FhirPatientProjection.ToEncounters(null));
        }
    }
}
