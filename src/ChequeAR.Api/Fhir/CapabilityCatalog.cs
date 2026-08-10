using System.Collections.Generic;

namespace ChequeAR.Api.Fhir
{
    /// Fuente unica de verdad del CapabilityStatement del server.
    ///
    /// La consumen dos lugares que antes tenian cada uno su copia literal:
    ///   - Controllers/Api/MetadataController  -> GET /fhir/r5/metadata (JSON FHIR)
    ///   - Controllers/Mvc/AdminController     -> pantalla F2 (vista HTML)
    ///
    /// Si se agrega un recurso o una operacion, se agrega aca y las dos salidas
    /// quedan sincronizadas solas.
    public static class CapabilityCatalog
    {
        public const string FhirVersion = "5.0.0";
        public const string SoftwareName = "ChequeAR";
        public const string SoftwareVersion = "0.1.0";

        public sealed class OperationCap
        {
            public string Name { get; set; }
            public string Definition { get; set; }
        }

        public sealed class ResourceCap
        {
            public string Type { get; set; }
            public IList<string> Interactions { get; set; } = new List<string>();
            public IList<OperationCap> Operations { get; set; } = new List<OperationCap>();

            /// <summary>
            /// Nota para quien integra. Se usa sobre todo para declarar explícitamente lo que
            /// NO se soporta: un recurso sin interacciones y con esta nota comunica una
            /// decisión; omitirlo del catálogo se leería como un olvido.
            /// </summary>
            public string Documentation { get; set; }
        }

        public static IReadOnlyList<ResourceCap> Resources { get; } = new List<ResourceCap>
        {
            new ResourceCap
            {
                Type = "Questionnaire",
                Interactions = new List<string> { "read", "search-type", "create", "update", "delete" },
                Operations = new List<OperationCap>
                {
                    new OperationCap { Name = "publish", Definition = "https://chequear.garrahan/OperationDefinition/publish" },
                    new OperationCap { Name = "archive", Definition = "https://chequear.garrahan/OperationDefinition/archive" }
                }
            },
            new ResourceCap
            {
                Type = "QuestionnaireResponse",
                Interactions = new List<string> { "read", "search-type", "create", "update" },
                Operations = new List<OperationCap>
                {
                    new OperationCap { Name = "finalize", Definition = "https://chequear.garrahan/OperationDefinition/finalize" }
                }
            },
            new ResourceCap
            {
                Type = "AuditEvent",
                Interactions = new List<string> { "read", "search-type" }
            },
            new ResourceCap
            {
                Type = "Patient",
                Interactions = new List<string> { "read", "search-type" },
                Documentation = "Sólo lectura. ChequeAR no posee la entidad: la deriva de la vista "
                              + "del hospital y, cuando esa fuente no responde, de sus propias fichas."
            },
            new ResourceCap
            {
                // Sin 'read': un encuentro derivado de una ficha no tiene identidad propia
                // estable que se pueda prometer como dirección individual.
                Type = "Encounter",
                Interactions = new List<string> { "search-type" },
                Documentation = "Sólo búsqueda por paciente. Derivado de las fichas cargadas; "
                              + "ChequeAR no registra episodios de atención."
            },
            new ResourceCap
            {
                // Declarado SIN interacciones a propósito: es la forma de decir "conozco este
                // tipo y no lo soporto". Omitirlo del catálogo se leería como un olvido.
                Type = "Observation",
                Documentation = "NO SOPORTADO. ChequeAR no produce observaciones; las consume del "
                              + "servidor FHIR del hospital al precargar datos de una ficha."
            }
        };
    }
}
