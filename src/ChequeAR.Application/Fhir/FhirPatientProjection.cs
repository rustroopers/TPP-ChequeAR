using System;
using System.Collections.Generic;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using Hl7.Fhir.Model;
using DomainResponse = ChequeAR.Domain.Entities.QuestionnaireResponse;

namespace ChequeAR.Application.Fhir
{
    /// <summary>
    /// Proyecta al estándar dos entidades que ChequeAR **no posee**: el paciente y el
    /// encuentro. El paciente viene de la vista del hospital o —en modo degradado— se deriva
    /// de las fichas propias; el encuentro se deriva siempre de las fichas.
    ///
    /// Por eso ambos son de sólo lectura y se identifican por identificador de historia
    /// clínica, nunca por una referencia resoluble: prometer una referencia que no podemos
    /// resolver sería mentirle a quien integra.
    /// </summary>
    public static class FhirPatientProjection
    {
        public const string HospitalIdSystem = "https://chequear.garrahan/patient-hospital-id";

        public static Patient ToPatient(PatientSearchHit hit)
        {
            if (hit == null) return null;

            var patient = new Patient
            {
                Id = hit.HospitalId,
                Identifier = { new Identifier(HospitalIdSystem, hit.HospitalId) }
            };

            if (!string.IsNullOrWhiteSpace(hit.FullName))
                patient.Name.Add(new HumanName { Text = hit.FullName });

            return patient;
        }

        public static IEnumerable<Patient> ToPatients(IEnumerable<PatientSearchHit> hits)
        {
            foreach (var h in hits ?? new List<PatientSearchHit>())
            {
                var p = ToPatient(h);
                if (p != null) yield return p;
            }
        }

        /// <summary>
        /// Un encuentro por ficha cargada. ChequeAR no registra episodios de atención: los
        /// deriva de lo que sus propios usuarios completaron, así que el encuentro es una
        /// proyección y no un registro con vida propia.
        /// </summary>
        public static Encounter ToEncounter(DomainResponse r)
        {
            if (r == null) return null;

            return new Encounter
            {
                Id = r.Id.ToString(),
                Status = EncounterStatus.Completed,
                Subject = new ResourceReference
                {
                    Identifier = new Identifier(HospitalIdSystem, r.PatientHospitalId),
                    Display = r.PatientHospitalId
                },
                ActualPeriod = new Period
                {
                    Start = r.AuthoredAt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
                }
            };
        }

        public static IEnumerable<Encounter> ToEncounters(IEnumerable<DomainResponse> responses)
        {
            foreach (var r in responses ?? new List<DomainResponse>())
            {
                var e = ToEncounter(r);
                if (e != null) yield return e;
            }
        }
    }
}
