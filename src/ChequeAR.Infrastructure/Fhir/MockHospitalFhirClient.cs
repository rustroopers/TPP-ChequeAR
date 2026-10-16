using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;

namespace ChequeAR.Infrastructure.Fhir
{
    /// Responde la via FHIR del hospital sin servidor FHIR detras. Se registra cuando
    /// HospitalFhir:BaseUrl esta vacio (ver ServiceComposition), igual que los otros mocks.
    ///
    /// No inventa un dataset propio: proyecta el MISMO que sirve las vistas VW_*, leyendolo
    /// por IOracleViewClient. Asi el paciente PAC-000007 cuenta la misma historia por las dos
    /// vias.
    ///
    /// Los recursos se arman con POCOs de Firely y se serializan con FhirJsonSerializer, no
    /// con strings a mano: lo que sale es FHIR R5 estructuralmente valido, verificable por el
    /// mismo validador que la app usa para el trafico real.
    public class MockHospitalFhirClient : IHospitalFhirClient
    {
        private const string LoincSystem = "http://loinc.org";
        private const string Cie10System = "http://hl7.org/fhir/sid/icd-10";
        private const string PatientIdSystem = "urn:chequear:hospital:paciente";

        private static readonly FhirJsonSerializer Serializer = new FhirJsonSerializer();

        private readonly IOracleViewClient _views;

        public MockHospitalFhirClient(IOracleViewClient views)
        {
            _views = views ?? throw new ArgumentNullException(nameof(views));
        }

        public async Task<string> GetPatientAsync(string hospitalId)
        {
            var rowsPaciente = await ByPatient("VW_PACIENTE", hospitalId);
            var row = rowsPaciente.Count > 0 ? rowsPaciente[0] : null;
            // null = 404, misma convencion que HospitalFhirClient. IntegrationHealthService
            // pinguea con "__ping__": sin fila igual devuelve null y el probe sale Ok.
            if (row == null) return null;

            return Serializer.SerializeToString(ToPatient(row));
        }

        public async Task<string> GetLastObservationAsync(string hospitalId, string code)
        {
            var rows = await ByPatient("VW_OBSERVACION_LAB", hospitalId);

            var latest = rows
                .Where(r => string.IsNullOrEmpty(code)
                            || string.Equals(Text(r, "COD_LOINC"), code, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => Date(r, "FECHA_TOMA") ?? DateTime.MinValue)
                .Take(1)
                .Select(ToObservation);

            // El server real responde el bundle de busqueda completo, incluso vacio: quien
            // consume espera un searchset, no un recurso suelto ni null.
            return Serializer.SerializeToString(SearchSet(latest));
        }

        public async Task<string> SearchConditionsAsync(string hospitalId)
        {
            var rows = await ByPatient("VW_DIAGNOSTICO", hospitalId);
            return Serializer.SerializeToString(SearchSet(rows.Select(ToCondition)));
        }

        // ---- Proyeccion VW_* -> FHIR R5 ----

        private static Patient ToPatient(IDictionary<string, object> row)
        {
            var id = Text(row, "ID_PACIENTE");
            var patient = new Patient
            {
                Id = id,
                Active = true,
                Gender = ToGender(Text(row, "SEXO"))
            };

            patient.Identifier.Add(new Identifier(PatientIdSystem, id));

            var documento = Text(row, "DOCUMENTO");
            if (!string.IsNullOrEmpty(documento))
            {
                patient.Identifier.Add(new Identifier(
                    "urn:chequear:hospital:" + (Text(row, "TIPO_DOCUMENTO") ?? "DOC").ToLowerInvariant(),
                    documento));
            }

            var apellido = Text(row, "APELLIDO");
            var nombre = Text(row, "NOMBRE");
            if (!string.IsNullOrEmpty(apellido) || !string.IsNullOrEmpty(nombre))
            {
                var name = new HumanName { Family = apellido };
                if (!string.IsNullOrEmpty(nombre)) name.Given = new[] { nombre };
                patient.Name.Add(name);
            }

            var nacimiento = Date(row, "FECHA_NACIMIENTO");
            if (nacimiento.HasValue)
                patient.BirthDate = nacimiento.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            return patient;
        }

        private static Observation ToObservation(IDictionary<string, object> row)
        {
            var obs = new Observation
            {
                Id = Text(row, "ID_OBSERVACION"),
                Status = ObservationStatus.Final,
                Code = Concept(LoincSystem, Text(row, "COD_LOINC"), Text(row, "DISPLAY")),
                Subject = PatientRef(Text(row, "ID_PACIENTE"))
            };

            var tomada = Date(row, "FECHA_TOMA");
            if (tomada.HasValue)
                obs.Effective = new FhirDateTime(new DateTimeOffset(tomada.Value, TimeSpan.Zero));

            var numerico = Number(row, "VALOR_NUMERICO");
            if (numerico.HasValue)
            {
                obs.Value = new Quantity
                {
                    Value = numerico.Value,
                    Unit = Text(row, "UNIDAD"),
                    System = "http://unitsofmeasure.org",
                    Code = Text(row, "UNIDAD")
                };
            }
            else
            {
                var texto = Text(row, "VALOR_TEXTO");
                if (!string.IsNullOrEmpty(texto)) obs.Value = new FhirString(texto);
            }

            return obs;
        }

        private static Condition ToCondition(IDictionary<string, object> row)
        {
            var cond = new Condition
            {
                Id = Text(row, "ID_DIAGNOSTICO"),
                Code = Concept(Cie10System, Text(row, "COD_CIE10"), Text(row, "DISPLAY")),
                Subject = PatientRef(Text(row, "ID_PACIENTE"))
            };

            var fecha = Date(row, "FECHA_DIAGNOSTICO");
            if (fecha.HasValue)
                cond.Onset = new FhirDateTime(new DateTimeOffset(fecha.Value, TimeSpan.Zero));

            return cond;
        }

        private static Bundle SearchSet(IEnumerable<Resource> resources)
        {
            var bundle = new Bundle { Type = Bundle.BundleType.Searchset };
            foreach (var r in resources)
                bundle.Entry.Add(new Bundle.EntryComponent { Resource = r });
            bundle.Total = bundle.Entry.Count;
            return bundle;
        }

        // ---- Helpers ----

        private async Task<IReadOnlyList<IDictionary<string, object>>> ByPatient(string view, string hospitalId)
        {
            if (string.IsNullOrWhiteSpace(hospitalId))
                return new List<IDictionary<string, object>>();

            return await _views.QueryAsync(view,
                new Dictionary<string, object> { { "ID_PACIENTE", hospitalId } });
        }

        private static AdministrativeGender? ToGender(string sexo)
        {
            switch ((sexo ?? "").ToUpperInvariant())
            {
                case "M": return AdministrativeGender.Male;
                case "F": return AdministrativeGender.Female;
                case "O": return AdministrativeGender.Other;
                default: return AdministrativeGender.Unknown;
            }
        }

        private static CodeableConcept Concept(string system, string code, string display)
            => string.IsNullOrEmpty(code) ? null : new CodeableConcept(system, code, display, display);

        private static ResourceReference PatientRef(string hospitalId)
            => string.IsNullOrEmpty(hospitalId) ? null : new ResourceReference("Patient/" + hospitalId);

        private static string Text(IDictionary<string, object> row, string column)
            => row.TryGetValue(column, out var v) && v != null ? v.ToString() : null;

        private static DateTime? Date(IDictionary<string, object> row, string column)
        {
            if (!row.TryGetValue(column, out var v) || v == null) return null;
            if (v is DateTime dt) return dt;
            return DateTime.TryParse(v.ToString(), CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed) ? parsed : (DateTime?)null;
        }

        private static decimal? Number(IDictionary<string, object> row, string column)
        {
            if (!row.TryGetValue(column, out var v) || v == null) return null;
            if (v is decimal d) return d;
            return decimal.TryParse(v.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : (decimal?)null;
        }
    }
}
